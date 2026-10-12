using System.Diagnostics;
using HRManagement;
using HRManagement.Models;
using HRManagement.Seeder;
using HRManagement.Services.Companies;
using HRManagement.Services.Emails;
using HRManagement.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

// Usage (from the repository root):
//   dotnet run --project Tools/HRManagement.Seeder -- [--companies 20] [--employees 250] [--days 60] [--password Perf@12345]
//   dotnet run --project Tools/HRManagement.Seeder -- --clean
// --connection "<connection string>" overrides ConnectionStrings:MyCnn from the API's appsettings.json.

var arguments = ParseArguments(args);
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .Build();
var connectionString = arguments.GetValueOrDefault("connection") ?? configuration.GetConnectionString("MyCnn")
    ?? throw new InvalidOperationException("No connection string: pass --connection or keep the API's appsettings.json next to the tool.");

var target = new SqlConnectionStringBuilder(connectionString);
Console.WriteLine($"Database: {target.DataSource} / {target.InitialCatalog}");

var services = new ServiceCollection()
    // EF logs every foreign-key refusal during --clean, which the retry loop expects; real failures still throw
    .AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning)
        .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None))
    .AddHttpContextAccessor()
    .AddDbContext<HrmsDbContext>(o => o.UseSqlServer(connectionString, sql => sql.CommandTimeout(600)))
    .AddScoped<ITenantContext, TenantContext>()
    .AddScoped<IEmailService, NoopEmailService>()
    .AddScoped<ICompanyService, CompanyService>()
    .BuildServiceProvider();

if (arguments.ContainsKey("clean"))
{
    await CleanAsync(services);
    return;
}

var options = new SeedOptions(
    Companies: int.Parse(arguments.GetValueOrDefault("companies") ?? "20"),
    EmployeesPerCompany: Math.Max(2, int.Parse(arguments.GetValueOrDefault("employees") ?? "250")),
    Days: int.Parse(arguments.GetValueOrDefault("days") ?? "60"),
    Password: arguments.GetValueOrDefault("password") ?? "Perf@12345");

// Numbering continues after the generated companies already present, so the tool can be run again to add more
int existing;
using (var scope = services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HrmsDbContext>();
    existing = (await db.Companies.Where(c => c.CompanyCode.StartsWith(CompanySeeder.CodePrefix)).Select(c => c.CompanyCode).ToListAsync())
        .Select(c => int.TryParse(c[CompanySeeder.CodePrefix.Length..], out var n) ? n : 0)
        .DefaultIfEmpty(0)
        .Max();
}

Console.WriteLine($"Creating {options.Companies} companies × {options.EmployeesPerCompany} employees, attendance for the last {options.Days} days...");

// One hash for every generated account: BCrypt is deliberately slow, hashing thousands of passwords would take minutes
var passwordHash = BCrypt.Net.BCrypt.HashPassword(options.Password);
var seeder = new CompanySeeder(services, options, passwordHash);
var total = Stopwatch.StartNew();
var totals = new int[5];

for (var n = existing + 1; n <= existing + options.Companies; n++)
{
    var watch = Stopwatch.StartNew();
    var stats = await seeder.SeedAsync(n);
    totals[0] += stats.Employees; totals[1] += stats.Attendance; totals[2] += stats.ShiftAssignments;
    totals[3] += stats.LeaveRequests; totals[4] += stats.OvertimeRequests;
    Console.WriteLine($"  {stats.Code}: {stats.Employees} employees, {stats.Attendance} attendance, {stats.ShiftAssignments} shift days, " +
                      $"{stats.LeaveRequests} leave, {stats.OvertimeRequests} overtime ({watch.Elapsed.TotalSeconds:F1}s)");
}

Console.WriteLine();
Console.WriteLine($"Done in {total.Elapsed:mm\\:ss}: {totals[0]} employees and accounts, {totals[1]} attendance records, " +
                  $"{totals[2]} shift assignments, {totals[3]} leave requests, {totals[4]} overtime requests.");
Console.WriteLine($"Sign in with any generated account, password {options.Password}:");
Console.WriteLine($"  admin   {CompanySeeder.CodePrefix.ToLowerInvariant()}{existing + 1:000}.admin");
Console.WriteLine($"  manager {CompanySeeder.CodePrefix.ToLowerInvariant()}{existing + 1:000}.emp0002   (head of the first department)");
Console.WriteLine($"  staff   {CompanySeeder.CodePrefix.ToLowerInvariant()}{existing + 1:000}.emp{Math.Min(50, options.EmployeesPerCompany):0000}");

static Dictionary<string, string?> ParseArguments(string[] args)
{
    var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--"))
            continue;
        var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
        result[args[i][2..]] = hasValue ? args[++i] : null;
    }
    return result;
}

// Removes every generated company (code PERF...) with everything that belongs to it, including rows the app
// created later while testing against it (payroll, notifications, audit logs...).
static async Task CleanAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HrmsDbContext>();

    var ids = await db.Companies.Where(c => c.CompanyCode.StartsWith(CompanySeeder.CodePrefix)).Select(c => c.CompanyId).ToListAsync();
    if (ids.Count == 0)
    {
        Console.WriteLine("No generated companies to remove.");
        return;
    }
    Console.WriteLine($"Removing {ids.Count} generated companies...");
    var idList = string.Join(",", ids);

    var statements = new List<string>
    {
        // Departments and employees point at each other; cut those links before deleting either
        $"UPDATE [Departments] SET [ManagerID] = NULL, [ParentDepartmentID] = NULL WHERE [CompanyID] IN ({idList})",
        $"UPDATE [Employees] SET [ManagerID] = NULL WHERE [CompanyID] IN ({idList})",
        $"DELETE ur FROM [UserRoles] ur JOIN [Users] u ON u.[UserID] = ur.[UserID] WHERE u.[CompanyID] IN ({idList})",
        $"DELETE rp FROM [RolePermissions] rp JOIN [Roles] r ON r.[RoleID] = rp.[RoleID] WHERE r.[CompanyID] IN ({idList})",
        $"DELETE rm FROM [RoleMenus] rm JOIN [Roles] r ON r.[RoleID] = rm.[RoleID] WHERE r.[CompanyID] IN ({idList})",
    };
    var deletes = db.Model.GetEntityTypes()
        .Where(t => typeof(ICompanyScoped).IsAssignableFrom(t.ClrType))
        .Select(t => $"DELETE FROM [{t.GetTableName()}] WHERE [CompanyID] IN ({idList})")
        .Append($"DELETE FROM [Users] WHERE [CompanyID] IN ({idList})")
        .Append($"DELETE FROM [Companies] WHERE [CompanyID] IN ({idList})")
        .ToList();

    db.Database.SetCommandTimeout(600);
    foreach (var sql in statements)
        await db.Database.ExecuteSqlRawAsync(sql);

    // Tables reference each other in many ways; keep deleting whatever no longer has rows depending on it
    for (var pass = 1; deletes.Count > 0; pass++)
    {
        var blocked = new List<string>();
        foreach (var sql in deletes)
        {
            try { await db.Database.ExecuteSqlRawAsync(sql); }
            catch (SqlException ex) when (ex.Number == 547) { blocked.Add(sql); }
        }
        if (blocked.Count == deletes.Count)
            throw new InvalidOperationException("Could not remove the remaining rows, a foreign key keeps blocking:\n" + string.Join("\n", blocked));
        deletes = blocked;
    }

    Console.WriteLine("Done.");
}
