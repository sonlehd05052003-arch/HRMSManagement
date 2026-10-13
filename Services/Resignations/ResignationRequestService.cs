using HRManagement.DTOs.LeaveRequest;
using HRManagement.DTOs.ResignationRequest;
using HRManagement.Models;
using HRManagement.Services.Approvals;
using HRManagement.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Services.Resignations
{
    public class ResignationRequestService : IResignationRequestService
    {
        private readonly HrmsDbContext _context;
        private readonly IApprovalRouteService _approvalRouteService;
        private readonly INotificationService _notifications;

        public ResignationRequestService(HrmsDbContext context, IApprovalRouteService approvalRouteService, INotificationService notifications)
        {
            _context = context;
            _approvalRouteService = approvalRouteService;
            _notifications = notifications;
        }

        public async Task<ServiceResult<ResignationRequestResponseDto>> CreateAsync(int userId, CreateResignationRequestDto dto)
        {
            var user = await _context.Users
                .Include(u => u.Employee)
                .FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);

            if (user == null || user.Employee == null)
                return ServiceResult<ResignationRequestResponseDto>.Fail("RR-01", "Không tìm thấy thông tin nhân viên.");

            var employee = user.Employee;

            // Validate notice period
            var noticeDays = 30;
            var noticeSetting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.SettingKey == "ResignationNoticeDays");
            if (noticeSetting != null && int.TryParse(noticeSetting.SettingValue, out var parsedDays))
                noticeDays = parsedDays;

            var minDate = DateOnly.FromDateTime(AppTime.Today.AddDays(noticeDays));
            if (dto.ExpectedLastWorkingDate < minDate)
                return ServiceResult<ResignationRequestResponseDto>.Fail("RR-02",
                    $"Ngày làm việc cuối phải cách ngày nộp đơn ít nhất {noticeDays} ngày (tối thiểu {minDate:dd/MM/yyyy}).");

            // Unique check
            var existing = await _context.ResignationRequests
                .FirstOrDefaultAsync(r => r.EmployeeId == employee.EmployeeId
                    && (r.Status == "Pending" || r.Status == "Approved"));
            if (existing != null)
                return ServiceResult<ResignationRequestResponseDto>.Fail("RR-03",
                    "Bạn đã có đơn xin thôi việc đang chờ xử lý. Vui lòng hủy đơn cũ trước khi nộp đơn mới.");

            // Approval route
            var approverId = await _approvalRouteService.GetApproverIdAsync(employee.EmployeeId);
            if (approverId == null)
                return ServiceResult<ResignationRequestResponseDto>.Fail("RR-04",
                    "Không tìm thấy người phê duyệt. Vui lòng liên hệ HR/Admin.");

            // Validate handover employee
            if (dto.HandoverToEmployeeId.HasValue)
            {
                var handoverExists = await _context.Employees
                    .AnyAsync(e => e.EmployeeId == dto.HandoverToEmployeeId.Value);
                if (!handoverExists)
                    return ServiceResult<ResignationRequestResponseDto>.Fail("RR-05",
                        "Nhân viên nhận bàn giao không tồn tại.");
            }

            // Count incomplete tasks
            var incompleteTaskCount = await _context.Tasks
                .CountAsync(t => t.AssignedTo == userId
                    && t.Status != "Completed"
                    && t.Status != "Cancelled");

            // Generate request number
            var today = AppTime.Today;
            var prefix = $"RR-{today:yyyyMMdd}-";
            var countToday = await _context.ResignationRequests
                .CountAsync(r => r.RequestNumber.StartsWith(prefix));
            var requestNumber = $"{prefix}{(countToday + 1):D4}";

            var request = new ResignationRequest
            {
                RequestNumber = requestNumber,
                EmployeeId = employee.EmployeeId,
                ExpectedLastWorkingDate = dto.ExpectedLastWorkingDate,
                Reason = dto.Reason,
                HandoverNote = dto.HandoverNote,
                HandoverToEmployeeId = dto.HandoverToEmployeeId,
                Status = "Pending",
                SubmittedDate = AppTime.Now,
                TargetApproverId = approverId
            };

            _context.ResignationRequests.Add(request);
            await _context.SaveChangesAsync();

            // After the save, so the notification points at the request's real id
            if (approverId.HasValue)
            {
                _notifications.Notify(approverId.Value, NotificationTypes.ResignationApproval, "Đơn xin thôi việc mới cần duyệt",
                    $"{employee.FullName} gửi đơn xin thôi việc {requestNumber}, ngày làm việc cuối dự kiến {request.ExpectedLastWorkingDate:dd/MM/yyyy}.",
                    "ResignationRequest", request.ResignationRequestId);
                await _context.SaveChangesAsync();
            }

            var responseDto = await BuildDto(request, incompleteTaskCount);
            return ServiceResult<ResignationRequestResponseDto>.Ok("RR-OK",
                incompleteTaskCount > 0
                    ? $"Đơn xin thôi việc đã được gửi thành công. Lưu ý: bạn còn {incompleteTaskCount} task chưa hoàn thành."
                    : "Đơn xin thôi việc đã được gửi thành công.",
                responseDto);
        }

        public async Task<ServiceResult<List<ResignationRequestResponseDto>>> GetMyRequestsAsync(int userId)
        {
            var user = await _context.Users
                .Include(u => u.Employee)
                .FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);

            if (user == null || user.Employee == null)
                return ServiceResult<List<ResignationRequestResponseDto>>.Fail("RR-01", "Không tìm thấy thông tin nhân viên.");

            var requests = await _context.ResignationRequests
                .Include(r => r.Employee)
                .Include(r => r.HandoverToEmployee)
                .Include(r => r.ReviewedByNavigation)
                .Where(r => r.EmployeeId == user.Employee.EmployeeId)
                .OrderByDescending(r => r.SubmittedDate)
                .ToListAsync();

            var dtos = new List<ResignationRequestResponseDto>();
            foreach (var r in requests)
            {
                var taskCount = await _context.Tasks
                    .CountAsync(t => t.AssignedTo == userId
                        && t.Status != "Completed"
                        && t.Status != "Cancelled");
                dtos.Add(await BuildDto(r, taskCount));
            }

            return ServiceResult<List<ResignationRequestResponseDto>>.Ok("RR-OK", "OK", dtos);
        }

        public async Task<ServiceResult<string>> CancelAsync(int userId, int requestId)
        {
            var user = await _context.Users
                .Include(u => u.Employee)
                .FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);

            if (user == null || user.Employee == null)
                return ServiceResult<string>.Fail("RR-01", "Không tìm thấy thông tin nhân viên.");

            var request = await _context.ResignationRequests
                .FirstOrDefaultAsync(r => r.ResignationRequestId == requestId
                    && r.EmployeeId == user.Employee.EmployeeId);

            if (request == null)
                return ServiceResult<string>.Fail("RR-06", "Không tìm thấy đơn xin thôi việc.");

            if (request.Status != "Pending")
                return ServiceResult<string>.Fail("RR-07", "Chỉ có thể hủy đơn đang ở trạng thái Chờ duyệt.");

            request.Status = "Cancelled";
            await _context.SaveChangesAsync();

            return ServiceResult<string>.Ok("RR-OK", "Đã hủy đơn xin thôi việc.", null);
        }

        public async Task<ServiceResult<List<ResignationRequestResponseDto>>> GetPendingForManagerAsync(int userId)
        {
            var requests = await _context.ResignationRequests
                .Include(r => r.Employee)
                .Include(r => r.HandoverToEmployee)
                .Include(r => r.ReviewedByNavigation)
                .Where(r => r.TargetApproverId == userId && r.Status == "Pending")
                .OrderByDescending(r => r.SubmittedDate)
                .ToListAsync();

            var dtos = new List<ResignationRequestResponseDto>();
            foreach (var r in requests)
            {
                // Get userId of the employee for task count
                var empUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.EmployeeId == r.EmployeeId && u.IsActive);
                var taskCount = empUser != null
                    ? await _context.Tasks.CountAsync(t => t.AssignedTo == empUser.UserId
                        && t.Status != "Completed" && t.Status != "Cancelled")
                    : 0;
                dtos.Add(await BuildDto(r, taskCount));
            }

            return ServiceResult<List<ResignationRequestResponseDto>>.Ok("RR-OK", "OK", dtos);
        }

        public async Task<ServiceResult<string>> ApproveAsync(int userId, int requestId, ApproveResignationRequestDto dto)
        {
            var request = await _context.ResignationRequests
                .FirstOrDefaultAsync(r => r.ResignationRequestId == requestId);

            if (request == null)
                return ServiceResult<string>.Fail("RR-06", "Không tìm thấy đơn xin thôi việc.");

            if (request.TargetApproverId != userId)
                return ServiceResult<string>.Fail("RR-08", "Bạn không có quyền phê duyệt đơn này.");

            if (request.Status != "Pending")
                return ServiceResult<string>.Fail("RR-07", "Đơn này không còn ở trạng thái chờ duyệt.");

            request.Status = "Approved";
            request.ReviewedDate = AppTime.Now;
            request.ReviewedBy = userId;
            request.ReviewerComments = dto.Comments;

            if (await _notifications.GetUserIdOfEmployeeAsync(request.EmployeeId) is { } requesterId)
                _notifications.Notify(requesterId, NotificationTypes.ResignationResult, "Đơn xin thôi việc đã được duyệt",
                    $"Đơn xin thôi việc {request.RequestNumber} đã được duyệt. HR sẽ liên hệ để làm thủ tục.",
                    "ResignationRequest", request.ResignationRequestId);

            await _context.SaveChangesAsync();

            return ServiceResult<string>.Ok("RR-OK", "Đã phê duyệt đơn xin thôi việc. HR có thể tạo thủ tục thôi việc.", null);
        }

        public async Task<ServiceResult<string>> RejectAsync(int userId, int requestId, RejectResignationRequestDto dto)
        {
            var request = await _context.ResignationRequests
                .FirstOrDefaultAsync(r => r.ResignationRequestId == requestId);

            if (request == null)
                return ServiceResult<string>.Fail("RR-06", "Không tìm thấy đơn xin thôi việc.");

            if (request.TargetApproverId != userId)
                return ServiceResult<string>.Fail("RR-08", "Bạn không có quyền từ chối đơn này.");

            if (request.Status != "Pending")
                return ServiceResult<string>.Fail("RR-07", "Đơn này không còn ở trạng thái chờ duyệt.");

            request.Status = "Rejected";
            request.ReviewedDate = AppTime.Now;
            request.ReviewedBy = userId;
            request.RejectionReason = dto.RejectionReason;

            if (await _notifications.GetUserIdOfEmployeeAsync(request.EmployeeId) is { } requesterId)
                _notifications.Notify(requesterId, NotificationTypes.ResignationResult, "Đơn xin thôi việc bị từ chối",
                    $"Đơn xin thôi việc {request.RequestNumber} đã bị từ chối. Lý do: {dto.RejectionReason}",
                    "ResignationRequest", request.ResignationRequestId);

            await _context.SaveChangesAsync();

            return ServiceResult<string>.Ok("RR-OK", "Đã từ chối đơn xin thôi việc.", null);
        }

        private async Task<ResignationRequestResponseDto> BuildDto(ResignationRequest r, int incompleteTaskCount)
        {
            string? handoverToName = null;
            if (r.HandoverToEmployeeId.HasValue)
            {
                if (r.HandoverToEmployee != null)
                    handoverToName = $"{r.HandoverToEmployee.FirstName} {r.HandoverToEmployee.LastName}";
                else
                {
                    var emp = await _context.Employees
                        .FirstOrDefaultAsync(e => e.EmployeeId == r.HandoverToEmployeeId.Value);
                    if (emp != null)
                        handoverToName = $"{emp.FirstName} {emp.LastName}";
                }
            }

            string? reviewedByName = null;
            if (r.ReviewedBy.HasValue)
            {
                if (r.ReviewedByNavigation != null)
                    reviewedByName = r.ReviewedByNavigation.Username;
                else
                {
                    var reviewer = await _context.Users
                        .FirstOrDefaultAsync(u => u.UserId == r.ReviewedBy.Value);
                    reviewedByName = reviewer?.Username;
                }
            }

            var employee = r.Employee ?? await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeId == r.EmployeeId);

            return new ResignationRequestResponseDto
            {
                ResignationRequestId = r.ResignationRequestId,
                RequestNumber = r.RequestNumber,
                EmployeeId = r.EmployeeId,
                EmployeeName = employee != null ? $"{employee.FirstName} {employee.LastName}" : "",
                EmployeeCode = employee?.EmployeeCode ?? "",
                ExpectedLastWorkingDate = r.ExpectedLastWorkingDate,
                Reason = r.Reason,
                HandoverNote = r.HandoverNote,
                HandoverToEmployeeId = r.HandoverToEmployeeId,
                HandoverToEmployeeName = handoverToName,
                Status = r.Status,
                RejectionReason = r.RejectionReason,
                ReviewerComments = r.ReviewerComments,
                ReviewedByName = reviewedByName,
                SubmittedDate = r.SubmittedDate,
                ReviewedDate = r.ReviewedDate,
                IncompleteTaskCount = incompleteTaskCount
            };
        }
    }
}
