using System.Security.Claims;
using System.Text.Json;
using HRManagement.Filters;
using HRManagement.Models;
using HRManagement.Services.CurrentUsers;
using HRManagement.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Tests.Filters
{
    public class GlobalExceptionHandlerTests
    {
        private readonly GlobalExceptionHandler _handler = new(NullLogger<GlobalExceptionHandler>.Instance);

        private static DefaultHttpContext NewContext(bool authenticated = false)
        {
            var http = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
            if (authenticated)
                http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1") }, "Test"));
            return http;
        }

        private async Task<(int Status, string Message)> HandleAsync(Exception exception, bool authenticated = false)
        {
            var http = NewContext(authenticated);
            Assert.True(await _handler.TryHandleAsync(http, exception, CancellationToken.None));

            http.Response.Body.Position = 0;
            using var json = await JsonDocument.ParseAsync(http.Response.Body);
            return (http.Response.StatusCode, json.RootElement.GetProperty("message").GetString()!);
        }

        private static Exception Capture(Action action)
        {
            try { action(); }
            catch (Exception ex) { return ex; }
            throw new InvalidOperationException("Expected an exception.");
        }

        // Exceptions have to come from the application assembly to count as "ours", so trigger real ones

        private static Exception NotSignedIn() =>
            Capture(() => new CurrentUserService(new HttpContextAccessor { HttpContext = new DefaultHttpContext() }, null!)
                .GetCurrentUserId());

        private static Exception SaveWithoutCompany()
        {
            var tenant = new TenantContext(new HttpContextAccessor());
            tenant.Use(null);
            using var db = new HrmsDbContext(
                new DbContextOptionsBuilder<HrmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
            db.Departments.Add(new Department { DepartmentCode = "X", DepartmentName = "X" });
            return Capture(() => db.SaveChanges());
        }

        [Fact]
        public async Task Business_error_from_our_code_returns_400_with_its_message()
        {
            var exception = SaveWithoutCompany();

            var (status, message) = await HandleAsync(exception);

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal(400, status);
            Assert.Equal(exception.Message, message);
        }

        [Fact]
        public async Task Same_exception_type_from_the_framework_is_hidden_behind_a_500()
        {
            var exception = Capture(() => new List<int>().First());

            var (status, message) = await HandleAsync(exception);

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal(500, status);
            Assert.DoesNotContain("Sequence", message);
        }

        [Fact]
        public async Task Unexpected_exception_returns_500_without_details()
        {
            var (status, message) = await HandleAsync(new NullReferenceException("secret internals"));

            Assert.Equal(500, status);
            Assert.DoesNotContain("secret", message);
        }

        [Fact]
        public async Task Not_signed_in_returns_401()
        {
            var (status, _) = await HandleAsync(NotSignedIn(), authenticated: false);

            Assert.Equal(401, status);
        }

        [Fact]
        public async Task Signed_in_but_not_allowed_returns_403_so_the_client_does_not_log_out()
        {
            var (status, _) = await HandleAsync(NotSignedIn(), authenticated: true);

            Assert.Equal(403, status);
        }
    }
}
