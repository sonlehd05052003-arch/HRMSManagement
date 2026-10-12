using Hangfire;
using HRManagement.Models;
using HRManagement.Services.HRProceduces;
using HRManagement.Services.Tenants;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Services.Backgrounds
{
    /// <summary>
    /// Job định kỳ (Hangfire, mỗi 5 phút): áp dụng các thủ tục nhân sự đã duyệt và đến ngày hiệu lực,
    /// lần lượt cho từng công ty đang hoạt động.
    /// </summary>
    public class HRProcedureBackgroundService
    {
        public const string JobId = "apply-pending-hr-procedures";

        private readonly HrmsDbContext _context;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<HRProcedureBackgroundService> _logger;

        public HRProcedureBackgroundService(
            HrmsDbContext context,
            IServiceScopeFactory scopeFactory,
            ILogger<HRProcedureBackgroundService> logger)
        {
            _context = context;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // Job chạy lại sau 5 phút nên không cần retry; không cho 2 lần chạy chồng nhau.
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 60)]
        public async Task ApplyPendingProceduresAsync()
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
                await ApplyForCompanyAsync(scope.ServiceProvider, companyId);
            }
        }

        private async Task ApplyForCompanyAsync(IServiceProvider services, int companyId)
        {
            var context = services.GetRequiredService<HrmsDbContext>();
            var procedureService = services.GetRequiredService<IHRProcedureService>();

            var today = AppTime.Today;

            // Lấy các procedure đã Approved, chưa Applied, và có EffectiveDate <= today
            var pendingApplyIds = await context.Hrprocedures
                .Where(p => p.Status == "Approved" && p.AppliedDate == null && p.EffectiveDate <= DateOnly.FromDateTime(today))
                .Select(p => p.ProcedureId)
                .ToListAsync();

            _logger.LogInformation("Company {CompanyId}: found {Count} procedures pending application.", companyId, pendingApplyIds.Count);

            foreach (var procedureId in pendingApplyIds)
            {
                try
                {
                    // Note: ApplyApprovedProcedureAsync usually takes currentUserId.
                    // In background context, we use 0 or a dedicated System User ID.
                    await procedureService.ApplyApprovedProcedureAsync(procedureId, 0);
                    _logger.LogInformation("Applied procedure ID {ProcedureId} successfully.", procedureId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to apply procedure ID {ProcedureId}.", procedureId);
                }
            }
        }
    }
}
