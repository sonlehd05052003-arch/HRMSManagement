using System.Security.Claims;
using System.Text.Json;
using HRManagement.Models;
using HRManagement.Services.Audits;
using HRManagement.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Tests.Audits
{
    public class AuditSaveChangesInterceptorTests
    {
        private const int CompanyId = 7;
        private const int UserId = 42;

        private readonly DbContextOptions<HrmsDbContext> _options;

        public AuditSaveChangesInterceptorTests()
        {
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, UserId.ToString()) }, "Test"))
            };
            var interceptor = new AuditSaveChangesInterceptor(
                new HttpContextAccessor { HttpContext = http }, NullLogger<AuditSaveChangesInterceptor>.Instance);

            _options = new DbContextOptionsBuilder<HrmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(interceptor)
                .Options;
        }

        private HrmsDbContext NewContext()
        {
            var tenant = new TenantContext(new HttpContextAccessor());
            tenant.Use(CompanyId);
            return new HrmsDbContext(_options, tenant);
        }

        private int AddBalance(decimal entitled)
        {
            using var db = NewContext();
            var balance = new LeaveBalance { EmployeeId = 1, LeaveTypeId = 1, Year = 2026, TotalEntitlement = entitled };
            db.LeaveBalances.Add(balance);
            db.SaveChanges();
            return balance.BalanceId;
        }

        private List<AuditLog> Logs()
        {
            using var db = NewContext();
            return db.AuditLogs.OrderBy(a => a.AuditId).ToList();
        }

        [Fact]
        public void Insert_is_logged_with_the_generated_id_and_the_user()
        {
            var id = AddBalance(12);

            var log = Assert.Single(Logs());
            Assert.Equal("INSERT", log.Action);
            Assert.Equal(id, log.RecordId);
            Assert.NotEqual(0, id);
            Assert.Equal(UserId, log.UserId);
            Assert.Equal(CompanyId, log.CompanyId);
            Assert.Null(log.OldValues);
        }

        [Fact]
        public async Task Update_logs_only_the_changed_values_before_and_after()
        {
            var id = AddBalance(12);
            await using (var db = NewContext())
            {
                var balance = await db.LeaveBalances.SingleAsync(b => b.BalanceId == id);
                balance.TotalEntitlement = 14;
                await db.SaveChangesAsync();
            }

            var update = Logs().Single(l => l.Action == "UPDATE");
            var before = JsonDocument.Parse(update.OldValues!).RootElement;
            var after = JsonDocument.Parse(update.NewValues!).RootElement;
            Assert.Equal(12, before.GetProperty("TotalEntitlement").GetDecimal());
            Assert.Equal(14, after.GetProperty("TotalEntitlement").GetDecimal());
            Assert.False(after.TryGetProperty("Year", out _));
            Assert.Equal(id, update.RecordId);
        }

        [Fact]
        public void Delete_keeps_the_old_values()
        {
            var id = AddBalance(12);
            using (var db = NewContext())
            {
                db.LeaveBalances.Remove(db.LeaveBalances.Single(b => b.BalanceId == id));
                db.SaveChanges();
            }

            var delete = Logs().Single(l => l.Action == "DELETE");
            Assert.Equal(id, delete.RecordId);
            Assert.Contains("\"TotalEntitlement\":12", delete.OldValues);
        }

        [Fact]
        public void Tables_outside_the_list_are_not_logged()
        {
            using (var db = NewContext())
            {
                db.Departments.Add(new Department { DepartmentCode = "D", DepartmentName = "D" });
                db.SaveChanges();
            }

            Assert.Empty(Logs());
        }
    }
}
