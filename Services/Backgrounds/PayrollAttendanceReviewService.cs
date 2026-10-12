using Hangfire;
using HRManagement.Models;
using HRManagement.Services.Payroll;
using HRManagement.Services.Tenants;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Services.Backgrounds
{
    /// <summary>
    /// Job định kỳ (Hangfire, mỗi giờ): tự chuyển kỳ lương sang giai đoạn xem xét chấm công
    /// khi đến AttendanceCutoffDate, lần lượt cho từng công ty đang hoạt động.
    /// </summary>
    public class PayrollAttendanceReviewService
    {
        public const string JobId = "trigger-payroll-attendance-review";

        private readonly HrmsDbContext _context;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PayrollAttendanceReviewService> _logger;

        public PayrollAttendanceReviewService(
            HrmsDbContext context,
            IServiceScopeFactory scopeFactory,
            ILogger<PayrollAttendanceReviewService> logger)
        {
            _context = context;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // Job chạy lại sau 1 giờ nên không cần retry; không cho 2 lần chạy chồng nhau (tránh gửi email trùng).
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 60)]
        public async Task TriggerDuePeriodReviewsAsync()
        {
            var companyIds = await _context.Companies
                .Where(c => c.IsActive)
                .Select(c => c.CompanyId)
                .ToListAsync();

            foreach (var companyId in companyIds)
            {
                // Mỗi công ty một scope riêng để DbContext và service chỉ thấy dữ liệu của công ty đó
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(companyId);
                await TriggerForCompanyAsync(scope.ServiceProvider, companyId);
            }
        }

        private async Task TriggerForCompanyAsync(IServiceProvider services, int companyId)
        {
            var context = services.GetRequiredService<HrmsDbContext>();
            var payrollService = services.GetRequiredService<IPayrollService>();

            var today = DateOnly.FromDateTime(AppTime.Today);

            // Tìm các kỳ lương Open có AttendanceCutoffDate = hôm nay
            var duePeriodIds = await context.PayrollPeriods
                .Where(p => p.Status == "Open" && p.AttendanceCutoffDate == today)
                .Select(p => p.PeriodId)
                .ToListAsync();

            _logger.LogInformation("Company {CompanyId}: found {Count} payroll periods due for attendance review trigger.", companyId, duePeriodIds.Count);

            foreach (var periodId in duePeriodIds)
            {
                try
                {
                    await payrollService.TriggerAttendanceReviewAsync(periodId);
                    _logger.LogInformation("Triggered attendance review for period {PeriodId}.", periodId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to trigger attendance review for period {PeriodId}.", periodId);
                }
            }
        }
    }
}
