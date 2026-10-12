using HRManagement.Models;

namespace HRManagement.Services.Audits
{
    public class AuditService : IAuditService
    {
        private readonly HrmsDbContext _context;

        public AuditService(HrmsDbContext context)
        {
            _context = context;
        }

        public async System.Threading.Tasks.Task TrackAsync(int? userId, string action, string description)
        {
            var log = new AuditLog
            {
                TableName = "Export",
                Action = action,
                RecordId = 0,
                UserId = userId,
                NewValues = description,
                ActionDate = AppTime.Now
            };

            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}

