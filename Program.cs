using Hangfire;
using HRManagement;
using HRManagement.Authorization;
using HRManagement.Configuration;
using HRManagement.DataAcess;
using HRManagement.DataAcess.Implementations;
using HRManagement.DataAcess.Interfaces;
using HRManagement.Filters;
using HRManagement.Mappers;
using HRManagement.Models;
using HRManagement.Services.Attendances;
using HRManagement.Services.Cloudinaries;
using HRManagement.Services.Companies;
using HRManagement.Services.CurrentUsers;
using HRManagement.Services.Departments;
using HRManagement.Services.Emails;
using HRManagement.Services.Employees;
using HRManagement.Services.FaceVerifications;
using HRManagement.Services.FileStorages;
using HRManagement.Services.HRProceduces;
using HRManagement.Services.Leaves;
using HRManagement.Services.Overtimes;
using HRManagement.Services.Positions;
using HRManagement.Services.Shifts;
using HRManagement.Services.Users;
using HRManagement.Services.Tasks;
using HRManagement.Services.Approvals;
using HRManagement.Services.Audits;
using HRManagement.Services.Exports;
using HRManagement.Services.Backgrounds;
using HRManagement.Services.Payroll;
using HRManagement.Services.Resignations;
using HRManagement.Services.Tenants;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Business time zone for AppTime.Now (defaults to Asia/Ho_Chi_Minh); never the server's own zone
AppTime.Configure(builder.Configuration["App:TimeZone"]);

// Database Configuration
// Every save of pay, attendance, leave balance, employee, role and setting rows is written to AuditLogs
builder.Services.AddSingleton<AuditSaveChangesInterceptor>();
builder.Services.AddDbContext<HrmsDbContext>((services, options) =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("MyCnn"))
        .AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>())
);

// Infrastructure
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("Cloudinary"));
builder.Services.AddAutoMapper(typeof(TaskProfile).Assembly);

// Repositories and application services (see Configuration/ServiceRegistration.cs)
builder.Services.AddRepositories();
builder.Services.AddApplicationServices();

// Background jobs (Hangfire, stored in the same SQL Server database under the HangFire schema)
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("MyCnn")));
builder.Services.AddHangfireServer();

builder.Services.AddScoped<HRProcedureBackgroundService>();
builder.Services.AddScoped<PayrollAttendanceReviewService>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        // In development allow any origin so the client can be opened from other devices on the LAN.
        if (builder.Environment.IsDevelopment())
            policy.SetIsOriginAllowed(_ => true);
        else
            policy.WithOrigins("http://localhost:5173", "https://app.peoplecore.tech");

        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// GET /health: whether the API can reach its database, for load balancers and uptime monitors
builder.Services.AddHealthChecks().AddDbContextCheck<HrmsDbContext>("database");

// Swagger/API Documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "HR Management API",
        Version = "v1",
        Description = "API Authentication with JWT for HR Management"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập token ở dạng: Bearer {token}"
    });

    c.OperationFilter<AuthorizeCheckOperationFilter>();
});

// Authentication & Authorization
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtSettings = builder.Configuration.GetSection("Jwt");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)
            )
        };
        options.Events = new JwtBearerEvents
        {
            // The client sends the token in an HttpOnly cookie; an Authorization header (Swagger, tools) still wins
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Request.Headers.Authorization)
                    && context.Request.Cookies.TryGetValue(AuthCookie.Name, out var cookieToken))
                {
                    context.Token = cookieToken;
                }
                return System.Threading.Tasks.Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var userIdStr = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var tokenLastLogin = principal?.FindFirst("LastLogin")?.Value;

                var tokenCompanyId = principal?.FindFirst(TenantContext.ClaimType)?.Value;

                // Tokens issued before multi-company support carry no company claim
                if (string.IsNullOrEmpty(tokenLastLogin) || tokenCompanyId == null)
                {
                    context.Fail("Invalid or outdated token format. Please re-login.");
                    return;
                }

                if (!string.IsNullOrEmpty(userIdStr) && int.TryParse(userIdStr, out int userId))
                {
                    // The request has no tenant yet at this point, so bypass the company filter
                    var dbContext = context.HttpContext.RequestServices.GetRequiredService<HrmsDbContext>();
                    var user = await dbContext.Users.IgnoreQueryFilters()
                        .AsNoTracking()
                        .Include(u => u.Company)
                        .FirstOrDefaultAsync(u => u.UserId == userId);

                    if (user == null || (user.CompanyId?.ToString() ?? "") != tokenCompanyId)
                    {
                        context.Fail("User no longer belongs to the company in this token.");
                        return;
                    }

                    // A company locked by the SuperAdmin loses access immediately, not only at next login
                    if (user.Company is { IsActive: false })
                    {
                        context.Fail("Company is locked.");
                        return;
                    }

                    if (user.LastLogin.HasValue)
                    {
                        var dbLastLogin = user.LastLogin.Value.ToString("yyyyMMddHHmmss");
                        if (dbLastLogin != tokenLastLogin)
                        {
                            context.Fail("Concurrent login detected. Session is no longer valid.");
                        }
                    }
                }

                // Sliding session: an active user gets a fresh cookie once the current token is half used.
                // Only for the cookie: a token sent in a header belongs to a tool that manages it itself.
                if (context.Result?.Failure == null
                    && principal != null
                    && string.IsNullOrEmpty(context.Request.Headers.Authorization)
                    && context.Request.Cookies.ContainsKey(AuthCookie.Name))
                {
                    var issuer = context.HttpContext.RequestServices.GetRequiredService<JwtTokenIssuer>();
                    if (issuer.TryRenew(principal, context.SecurityToken) is { } renewed)
                        AuthCookie.Append(context.HttpContext, renewed.Token, renewed.ExpiresUtc);
                }
            }
        };
    });

builder.Services.AddSingleton<JwtTokenIssuer>();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IPermissionCache, PermissionCache>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

// Per-IP limits on login and password reset (see RateLimitPolicies); the cache counts wrong OTP attempts
builder.Services.AddAuthRateLimits();
builder.Services.AddMemoryCache();

// Exceptions that escape a controller become a JSON { message } response (see GlobalExceptionHandler)
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Controllers & JSON configuration
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});

// File upload limits
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 5 * 1024 * 1024;
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 5 * 1024 * 1024;
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseRouting();

// Middleware Pipeline
app.UseCors("AllowAll");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Hangfire dashboard: only exposed in development and only to requests from this machine
    app.UseHangfireDashboard("/hangfire");
}

await SuperAdminSeeder.EnsureAsync(app.Services, app.Configuration, app.Logger);

var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
var jobOptions = new RecurringJobOptions { TimeZone = AppTime.TimeZone };
recurringJobs.AddOrUpdate<HRProcedureBackgroundService>(
    HRProcedureBackgroundService.JobId, job => job.ApplyPendingProceduresAsync(), "*/5 * * * *", jobOptions);
recurringJobs.AddOrUpdate<PayrollAttendanceReviewService>(
    PayrollAttendanceReviewService.JobId, job => job.TriggerDuePeriodReviewsAsync(), Cron.Hourly(), jobOptions);

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();
app.Run();
