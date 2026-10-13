using HRManagement.DTOs;
using HRManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Services.Notifications
{
    /// <summary>
    /// Values of Notification.NotificationType. "...Approval" goes to whoever must act on a request,
    /// "...Result" back to the person who sent it; the client picks the page to open from this.
    /// </summary>
    public static class NotificationTypes
    {
        public const string LeaveApproval = "LeaveApproval";
        public const string LeaveResult = "LeaveResult";
        public const string OvertimeApproval = "OvertimeApproval";
        public const string OvertimeResult = "OvertimeResult";
        public const string ResignationApproval = "ResignationApproval";
        public const string ResignationResult = "ResignationResult";
        public const string ExplanationApproval = "ExplanationApproval";
        public const string ExplanationResult = "ExplanationResult";
        public const string Payslip = "Payslip";
    }

    public interface INotificationService
    {
        /// <summary>Adds a notification to the current unit of work; it is saved with the caller's SaveChanges.</summary>
        void Notify(int recipientUserId, string type, string title, string message, string? relatedEntity = null, int? relatedEntityId = null);

        /// <summary>The account of an employee, if they have one.</summary>
        Task<int?> GetUserIdOfEmployeeAsync(int employeeId);

        /// <summary>The account of an employee's direct manager, if any.</summary>
        Task<int?> GetManagerUserIdAsync(int employeeId);

        Task<PagedResultDto<UserNotificationDto>> GetMineAsync(int userId, bool unreadOnly, int page, int pageSize);
        Task<int> CountUnreadAsync(int userId);
        Task<bool> MarkReadAsync(int userId, int notificationId);
        Task<int> MarkAllReadAsync(int userId);
    }

    public class NotificationService : INotificationService
    {
        private readonly HrmsDbContext _context;

        public NotificationService(HrmsDbContext context)
        {
            _context = context;
        }

        public void Notify(int recipientUserId, string type, string title, string message, string? relatedEntity = null, int? relatedEntityId = null)
        {
            _context.Notifications.Add(new Notification
            {
                RecipientUserId = recipientUserId,
                NotificationType = type,
                Title = title,
                Message = message,
                RelatedEntity = relatedEntity,
                RelatedEntityId = relatedEntityId,
                IsRead = false,
                SentDate = AppTime.Now
            });
        }

        public async Task<int?> GetUserIdOfEmployeeAsync(int employeeId) =>
            await _context.Users
                .Where(u => u.EmployeeId == employeeId && u.IsActive)
                .Select(u => (int?)u.UserId)
                .FirstOrDefaultAsync();

        public async Task<int?> GetManagerUserIdAsync(int employeeId)
        {
            var managerId = await _context.Employees
                .Where(e => e.EmployeeId == employeeId)
                .Select(e => e.ManagerId)
                .FirstOrDefaultAsync();
            return managerId.HasValue ? await GetUserIdOfEmployeeAsync(managerId.Value) : null;
        }

        public async Task<PagedResultDto<UserNotificationDto>> GetMineAsync(int userId, bool unreadOnly, int page, int pageSize)
        {
            var query = _context.Notifications.AsNoTracking().Where(n => n.RecipientUserId == userId);
            if (unreadOnly)
                query = query.Where(n => !n.IsRead);

            return new PagedResultDto<UserNotificationDto>
            {
                Page = page,
                PageSize = pageSize,
                Total = await query.CountAsync(),
                Items = await query
                    .OrderByDescending(n => n.SentDate).ThenByDescending(n => n.NotificationId)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(n => new UserNotificationDto
                    {
                        NotificationId = n.NotificationId,
                        NotificationType = n.NotificationType,
                        Title = n.Title,
                        Message = n.Message,
                        RelatedEntity = n.RelatedEntity,
                        RelatedEntityId = n.RelatedEntityId,
                        IsRead = n.IsRead,
                        SentDate = n.SentDate
                    })
                    .ToListAsync()
            };
        }

        public Task<int> CountUnreadAsync(int userId) =>
            _context.Notifications.CountAsync(n => n.RecipientUserId == userId && !n.IsRead);

        public async Task<bool> MarkReadAsync(int userId, int notificationId)
        {
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.NotificationId == notificationId && n.RecipientUserId == userId);
            if (notification == null)
                return false;

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadDate = AppTime.Now;
                await _context.SaveChangesAsync();
            }
            return true;
        }

        public Task<int> MarkAllReadAsync(int userId) =>
            _context.Notifications
                .Where(n => n.RecipientUserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadDate, AppTime.Now));
    }
}
