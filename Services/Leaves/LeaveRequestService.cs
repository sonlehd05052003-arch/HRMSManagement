using System.Text.Json;
using HRManagement.DTOs.LeaveRequest;
using HRManagement.Models;
using HRManagement.Services.CurrentUsers;
using HRManagement.Services.Approvals;
using HRManagement.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Services.Leaves
{
    public class LeaveRequestService : ILeaveRequestService
    {
        private readonly HrmsDbContext _context;
        private readonly ICurrentUserService _currentUserService;
        private readonly IApprovalRouteService _approvalRouteService;
        private readonly ITopLevelResolver _topLevelResolver;
        private readonly INotificationService _notifications;

        public LeaveRequestService(
            HrmsDbContext context, 
            ICurrentUserService currentUserService,
            IApprovalRouteService approvalRouteService,
            ITopLevelResolver topLevelResolver,
            INotificationService notifications)
        {
            _context = context;
            _currentUserService = currentUserService;
            _approvalRouteService = approvalRouteService;
            _topLevelResolver = topLevelResolver;
            _notifications = notifications;
        }
        public async Task<ServiceResult<LeaveRequestResponseDTO>> CreateLeaveRequestAsync(int userId, CreateLeaveRequestDTO dto)
        {
            var user = await _context.Users
                .Include(u => u.Employee)
                .FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);

            if (user == null || user.Employee == null)
            {
                return ServiceResult<LeaveRequestResponseDTO>.Fail("MSG-01", "User or employee not found.");
            }

            var employee = user.Employee;

            // Check employee status
            if (employee.EmploymentStatus != "Active")
            {
                return ServiceResult<LeaveRequestResponseDTO>.Fail("MSG-EMP-01", "Employee is not active.");
            }

            // Check join date
            if (employee.JoinDate > dto.StartDate)
            {
                return ServiceResult<LeaveRequestResponseDTO>.Fail("MSG-EMP-02", "Cannot create leave request before join date.");
            }

            if (dto.StartDate > dto.EndDate)
            {
                return ServiceResult<LeaveRequestResponseDTO>.Fail("MSG-02", "Start date cannot be later than end date.");
            }

            if (dto.NumberOfDays <= 0)
            {
                return ServiceResult<LeaveRequestResponseDTO>.Fail("MSG-03", "Number of days must be greater than 0.");
            }

            var leaveType = await _context.LeaveTypes
                .FirstOrDefaultAsync(x => x.LeaveTypeId == dto.LeaveTypeID && x.IsActive);

            if (leaveType == null)
            {
                return ServiceResult<LeaveRequestResponseDTO>.Fail("MSG-08", "Invalid leave type.");
            }

            var overlappingRequest = await _context.LeaveRequests
                .Where(x =>
                    x.EmployeeId == employee.EmployeeId &&
                    (x.Status == "Pending" || x.Status == "Approved") &&
                    dto.StartDate <= x.EndDate &&
                    dto.EndDate >= x.StartDate)
                .Select(x => new { x.StartDate, x.EndDate })
                .FirstOrDefaultAsync();

            if (overlappingRequest != null)
            {
                return ServiceResult<LeaveRequestResponseDTO>.Fail(
                    "MSG-09",
                    $"Selected dates overlap with an existing {overlappingRequest.StartDate:dd/MM/yyyy} to {overlappingRequest.EndDate:dd/MM/yyyy} leave request.");
            }

            decimal currentBalance = 0;
            decimal remainingAfterRequest = 0;
            LeaveBalance leaveBalance = null;

            if (leaveType.AnnualEntitlement > 0)
            {
                leaveBalance = await _context.LeaveBalances
                    .FirstOrDefaultAsync(x =>
                        x.EmployeeId == employee.EmployeeId &&
                        x.LeaveTypeId == dto.LeaveTypeID &&
                        x.Year == dto.StartDate.Year);

                if (leaveBalance == null)
                {
                    // Instead of failing, we'll assume 0 balance and let the user "Submit anyway" if they want
                    currentBalance = 0;
                    remainingAfterRequest = -dto.NumberOfDays;
                }
                else
                {
                    currentBalance = leaveBalance.RemainingDays
                        ?? leaveBalance.TotalEntitlement - leaveBalance.UsedDays + leaveBalance.CarriedForward;

                    remainingAfterRequest = currentBalance - dto.NumberOfDays;
                }

                if (currentBalance < dto.NumberOfDays && !dto.SubmitAnyway)
                {
                    return ServiceResult<LeaveRequestResponseDTO>.Fail(
                        "MSG-27",
                        "Insufficient leave balance.",
                        new LeaveRequestResponseDTO
                        {
                            EmployeeID = employee.EmployeeId,
                            LeaveTypeID = dto.LeaveTypeID,
                            LeaveTypeName = leaveType.LeaveTypeName,
                            StartDate = dto.StartDate,
                            EndDate = dto.EndDate,
                            NumberOfDays = dto.NumberOfDays,
                            Reason = dto.Reason,
                            CurrentBalance = currentBalance,
                            RemainingAfterRequest = remainingAfterRequest,
                            MessageCode = "MSG-27",
                            Message = "Insufficient leave balance."
                        });
                }
            }

            var authResult = await _approvalRouteService.CanSubmitRequestAsync(employee.EmployeeId);
            if (!authResult.IsAuthorized)
            {
                return ServiceResult<LeaveRequestResponseDTO>.Fail("MSG-41", authResult.Message ?? "Invalid approval route.");
            }

            var approverId = await _approvalRouteService.GetApproverIdAsync(employee.EmployeeId);
            string initialStatus = approverId.HasValue ? "Pending" : "Approved";

            var leaveRequest = new LeaveRequest
            {
                RequestNumber = await GenerateRequestNumberAsync(),
                EmployeeId = employee.EmployeeId,
                LeaveTypeId = dto.LeaveTypeID,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                NumberOfDays = dto.NumberOfDays,
                Reason = dto.Reason,
                Status = initialStatus,
                TargetApproverId = approverId,
                SubmittedDate = AppTime.Now,
                ReviewedDate = initialStatus == "Approved" ? AppTime.Now : null,
                ReviewedBy = initialStatus == "Approved" ? null : null, // System approved
                ReviewerComments = initialStatus == "Approved" ? "Auto-approved by system (Top-level no fallback)" : null,
                ApprovedDate = initialStatus == "Approved" ? AppTime.Now : null,
                ApprovedBy = null // System
            };

            _context.LeaveRequests.Add(leaveRequest);


            // If approved by system, we must deduct balance immediately
            if (initialStatus == "Approved" && leaveBalance != null)
            {
                leaveBalance.UsedDays += dto.NumberOfDays;
                leaveBalance.RemainingDays = (leaveBalance.TotalEntitlement + leaveBalance.CarriedForward) - leaveBalance.UsedDays;
                leaveBalance.LastUpdated = AppTime.Now;
            }

            await _context.SaveChangesAsync();

            // After the save, so the notification points at the request's real id
            if (approverId.HasValue)
                _notifications.Notify(approverId.Value, NotificationTypes.LeaveApproval, "Đơn nghỉ phép mới cần duyệt",
                    $"Đơn nghỉ phép {leaveRequest.RequestNumber} ({leaveRequest.StartDate:dd/MM}–{leaveRequest.EndDate:dd/MM}) đang chờ bạn duyệt.",
                    "LeaveRequest", leaveRequest.LeaveRequestId);

            var auditLog = new AuditLog
            {
                TableName = "LeaveRequests",
                Action = "INSERT",
                RecordId = leaveRequest.LeaveRequestId,
                UserId = userId,
                NewValues = JsonSerializer.Serialize(new
                {
                    leaveRequest.LeaveRequestId,
                    leaveRequest.RequestNumber,
                    leaveRequest.EmployeeId,
                    leaveRequest.LeaveTypeId,
                    leaveRequest.StartDate,
                    leaveRequest.EndDate,
                    leaveRequest.NumberOfDays,
                    leaveRequest.Reason,
                    leaveRequest.Status
                }),
                ActionDate = AppTime.Now
            };

            _context.AuditLogs.Add(auditLog);
            await _context.SaveChangesAsync();

            var response = new LeaveRequestResponseDTO
            {
                LeaveRequestID = leaveRequest.LeaveRequestId,
                RequestNumber = leaveRequest.RequestNumber,
                EmployeeID = leaveRequest.EmployeeId,
                LeaveTypeID = leaveRequest.LeaveTypeId,
                LeaveTypeName = leaveType.LeaveTypeName,
                StartDate = leaveRequest.StartDate,
                EndDate = leaveRequest.EndDate,
                NumberOfDays = leaveRequest.NumberOfDays,
                Reason = leaveRequest.Reason,
                Status = leaveRequest.Status,
                SubmittedDate = leaveRequest.SubmittedDate,
                CurrentBalance = currentBalance,
                RemainingAfterRequest = remainingAfterRequest,
                MessageCode = "MSG-22",
                Message = "Leave request submitted successfully."
            };

            return ServiceResult<LeaveRequestResponseDTO>.Ok("MSG-22", "Leave request submitted successfully.", response);
        }

        private async Task<string> GenerateRequestNumberAsync()
        {
            var today = AppTime.Now;
            string prefix = $"LR-{today:yyyyMMdd}";

            int countToday = await _context.LeaveRequests
                .CountAsync(x => x.SubmittedDate.Date == today.Date);

            return $"{prefix}-{countToday + 1:D3}";
        }
        public async Task<ServiceResult<string>> ApproveLeaveRequestAsync(int managerUserId,int leaveRequestId,
    ApproveLeaveRequestDTO dto)
        {
            var managerUser = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == managerUserId && x.IsActive);

            if (managerUser == null)
            {
                return ServiceResult<string>.Fail("MSG-41", "You do not have authority to process this request.");
            }

            var leaveRequest = await _context.LeaveRequests
                .FirstOrDefaultAsync(x => x.LeaveRequestId == leaveRequestId);

            if (leaveRequest == null)
            {
                return ServiceResult<string>.Fail("MSG-44", "Leave request not found.");
            }

            if (leaveRequest.Status != "Pending")
            {
                return ServiceResult<string>.Fail("MSG-42", "This request has already been processed.");
            }

            var employee = await _context.Employees
                .FirstOrDefaultAsync(x => x.EmployeeId == leaveRequest.EmployeeId);

            if (employee == null)
            {
                return ServiceResult<string>.Fail("MSG-45", "Employee not found.");
            }

            // Validation using frozen TargetApproverId
            if (leaveRequest.TargetApproverId == null || leaveRequest.TargetApproverId != managerUserId)
            {
                return ServiceResult<string>.Fail("MSG-41", "You do not have authority to process this request.");
            }

            var leaveType = await _context.LeaveTypes
                .FirstOrDefaultAsync(x => x.LeaveTypeId == leaveRequest.LeaveTypeId && x.IsActive);

            if (leaveType == null)
            {
                return ServiceResult<string>.Fail("MSG-08", "Invalid leave type.");
            }
            LeaveBalance leaveBalance = null;

            if (leaveType.AnnualEntitlement > 0)
            {
                int targetYear = leaveRequest.StartDate.Year;

                leaveBalance = await _context.LeaveBalances
                    .FirstOrDefaultAsync(x =>
                        x.EmployeeId == leaveRequest.EmployeeId &&
                        x.LeaveTypeId == leaveRequest.LeaveTypeId &&
                        x.Year == targetYear);

                if (leaveBalance == null)
                {
                    // Create a default balance if it doesn't exist to track the usage
                    leaveBalance = new LeaveBalance
                    {
                        EmployeeId = leaveRequest.EmployeeId,
                        LeaveTypeId = leaveRequest.LeaveTypeId,
                        Year = targetYear,
                        TotalEntitlement = leaveType.AnnualEntitlement,
                        UsedDays = 0,
                        CarriedForward = 0,
                        RemainingDays = leaveType.AnnualEntitlement,
                        LastUpdated = AppTime.Now
                    };
                    _context.LeaveBalances.Add(leaveBalance);
                }

                decimal currentBalance = leaveBalance.RemainingDays
                    ?? leaveBalance.TotalEntitlement - leaveBalance.UsedDays + leaveBalance.CarriedForward;

                decimal newBalance = currentBalance - leaveRequest.NumberOfDays;

                // Allow negative balance, but log it or handle it in Salary calculation later
                leaveBalance.UsedDays += leaveRequest.NumberOfDays;
                leaveBalance.RemainingDays = (leaveBalance.TotalEntitlement + leaveBalance.CarriedForward) - leaveBalance.UsedDays;
                leaveBalance.LastUpdated = AppTime.Now;
            }

            leaveRequest.Status = "Approved";
            leaveRequest.ApprovedDate = AppTime.Now;
            leaveRequest.ApprovedBy = managerUserId;
            leaveRequest.ReviewedDate = AppTime.Now;
            leaveRequest.ReviewedBy = managerUserId;
            leaveRequest.ReviewerComments = dto.Comments;
            leaveRequest.RejectionReason = null;

            var employeeUser = await _context.Users
                .FirstOrDefaultAsync(x => x.EmployeeId == leaveRequest.EmployeeId && x.IsActive);

            if (employeeUser != null)
                _notifications.Notify(employeeUser.UserId, NotificationTypes.LeaveResult, "Đơn nghỉ phép đã được duyệt",
                    $"Đơn nghỉ phép {leaveRequest.RequestNumber} ({leaveRequest.StartDate:dd/MM}–{leaveRequest.EndDate:dd/MM}) đã được duyệt.",
                    "LeaveRequest", leaveRequest.LeaveRequestId);

            var auditLog = new AuditLog
            {
                TableName = "LeaveRequests",
                Action = "UPDATE",
                RecordId = leaveRequest.LeaveRequestId,
                UserId = managerUserId,
                NewValues = JsonSerializer.Serialize(new
                {
                    leaveRequest.LeaveRequestId,
                    leaveRequest.Status,
                    leaveRequest.ApprovedDate,
                    leaveRequest.ApprovedBy,
                    leaveRequest.ReviewedDate,
                    leaveRequest.ReviewedBy,
                    leaveRequest.ReviewerComments,
                    UpdatedLeaveBalanceUsedDays = leaveBalance?.UsedDays
                }),
                ActionDate = AppTime.Now
            };

            _context.AuditLogs.Add(auditLog);

            await _context.SaveChangesAsync();

            return ServiceResult<string>.Ok("MSG-40", "Request approved successfully.", null);
        }
        public async Task<ServiceResult<string>> RejectLeaveRequestAsync(
    int managerUserId,
    int leaveRequestId,
    RejectLeaveRequestDTO dto)
        {
            var managerUser = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == managerUserId && x.IsActive);

            if (managerUser == null)
            {
                return ServiceResult<string>.Fail("MSG-41", "You do not have authority to process this request.");
            }

            var leaveRequest = await _context.LeaveRequests
                .FirstOrDefaultAsync(x => x.LeaveRequestId == leaveRequestId);

            if (leaveRequest == null)
            {
                return ServiceResult<string>.Fail("MSG-44", "Leave request not found.");
            }

            if (leaveRequest.Status != "Pending")
            {
                return ServiceResult<string>.Fail("MSG-42", "This request has already been processed.");
            }

            if (string.IsNullOrWhiteSpace(dto.RejectionReason))
            {
                return ServiceResult<string>.Fail("MSG-45", "Please provide a reason for rejection.");
            }

            var employee = await _context.Employees
                .FirstOrDefaultAsync(x => x.EmployeeId == leaveRequest.EmployeeId);

            if (employee == null)
            {
                return ServiceResult<string>.Fail("MSG-46", "Employee not found.");
            }

            // Validation using frozen TargetApproverId
            if (leaveRequest.TargetApproverId == null || leaveRequest.TargetApproverId != managerUserId)
            {
                return ServiceResult<string>.Fail("MSG-41", "You do not have authority to process this request.");
            }

            leaveRequest.Status = "Rejected";
            leaveRequest.RejectionReason = dto.RejectionReason;
            leaveRequest.ReviewedDate = AppTime.Now;
            leaveRequest.ReviewedBy = managerUserId;
            leaveRequest.ReviewerComments = dto.RejectionReason;
            leaveRequest.ApprovedDate = null;
            leaveRequest.ApprovedBy = null;

            var employeeUser = await _context.Users
                .FirstOrDefaultAsync(x => x.EmployeeId == leaveRequest.EmployeeId && x.IsActive);

            if (employeeUser != null)
                _notifications.Notify(employeeUser.UserId, NotificationTypes.LeaveResult, "Đơn nghỉ phép bị từ chối",
                    $"Đơn nghỉ phép {leaveRequest.RequestNumber} ({leaveRequest.StartDate:dd/MM}–{leaveRequest.EndDate:dd/MM}) đã bị từ chối.",
                    "LeaveRequest", leaveRequest.LeaveRequestId);

            var auditLog = new AuditLog
            {
                TableName = "LeaveRequests",
                Action = "UPDATE",
                RecordId = leaveRequest.LeaveRequestId,
                UserId = managerUserId,
                NewValues = JsonSerializer.Serialize(new
                {
                    leaveRequest.LeaveRequestId,
                    leaveRequest.Status,
                    leaveRequest.RejectionReason,
                    leaveRequest.ReviewedDate,
                    leaveRequest.ReviewedBy,
                    leaveRequest.ReviewerComments
                }),
                ActionDate = AppTime.Now
            };

            _context.AuditLogs.Add(auditLog);

            await _context.SaveChangesAsync();

            return ServiceResult<string>.Ok("MSG-44", "Request rejected successfully.", null);
        }
        public async Task<ServiceResult<string>> CancelLeaveRequestAsync(int userId,int leaveRequestId,CancelLeaveRequestDTO dto)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);

            if (user == null || user.EmployeeId == null)
            {
                return ServiceResult<string>.Fail("MSG-106", "Access Denied.");
            }

            var leaveRequest = await _context.LeaveRequests
                .FirstOrDefaultAsync(x => x.LeaveRequestId == leaveRequestId);

            if (leaveRequest == null)
            {
                return ServiceResult<string>.Fail("MSG-39", "Leave request not found.");
            }

            if (leaveRequest.EmployeeId != user.EmployeeId)
            {
                return ServiceResult<string>.Fail("MSG-106", "Access Denied.");
            }

            if (leaveRequest.Status != "Pending")
            {
                return ServiceResult<string>.Fail(
                    "MSG-38",
                    "Cannot cancel this request. It has already been approved/rejected/cancelled.");
            }

            leaveRequest.Status = "Cancelled";
            leaveRequest.ReviewerComments = dto.Reason;
            leaveRequest.RejectionReason = dto.Reason;
            leaveRequest.ReviewedDate = AppTime.Now;
            leaveRequest.ReviewedBy = userId;
            leaveRequest.ApprovedDate = null;
            leaveRequest.ApprovedBy = null;

            var managerUser = await _context.Users
                .FirstOrDefaultAsync(x => x.EmployeeId == user.EmployeeId && x.IsActive);

            var auditLog = new AuditLog
            {
                TableName = "LeaveRequests",
                Action = "UPDATE",
                RecordId = leaveRequest.LeaveRequestId,
                UserId = userId,
                NewValues = JsonSerializer.Serialize(new
                {
                    leaveRequest.LeaveRequestId,
                    leaveRequest.Status,
                    CancelReason = dto.Reason,
                    leaveRequest.ReviewedDate,
                    leaveRequest.ReviewedBy
                }),
                ActionDate = AppTime.Now
            };

            _context.AuditLogs.Add(auditLog);

            await _context.SaveChangesAsync();

            return ServiceResult<string>.Ok("MSG-37", "Request cancelled successfully.", null);
        }
        public async Task<ServiceResult<List<MyLeaveRequestItemDTO>>> GetMyLeaveRequestsAsync(int userId)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);

            if (user == null || user.EmployeeId == null)
            {
                return ServiceResult<List<MyLeaveRequestItemDTO>>.Ok(
                    "MSG-00", 
                    "User has no employee profile, so they have no personal leave requests.", 
                    new List<MyLeaveRequestItemDTO>()
                );
            }

            var requests = await _context.LeaveRequests
                .Where(x => x.EmployeeId == user.EmployeeId)
                .Join(
                    _context.LeaveTypes,
                    request => request.LeaveTypeId,
                    leaveType => leaveType.LeaveTypeId,
                    (request, leaveType) => new MyLeaveRequestItemDTO
                    {
                        LeaveRequestID = request.LeaveRequestId,
                        RequestNumber = request.RequestNumber,
                        LeaveTypeID = request.LeaveTypeId,
                        LeaveTypeName = leaveType.LeaveTypeName,
                        StartDate = request.StartDate,
                        EndDate = request.EndDate,
                        NumberOfDays = request.NumberOfDays,
                        Reason = request.Reason,
                        Status = request.Status,
                        SubmittedDate = request.SubmittedDate
                    }
                )
                .OrderByDescending(x => x.SubmittedDate)
                .ToListAsync();

            return ServiceResult<List<MyLeaveRequestItemDTO>>.Ok("", "Success", requests);
        }
        public async Task<ServiceResult<IEnumerable<TeamLeaveCalendarDTO>>> GetTeamLeaveCalendarAsync(int managerUserId)
        {
            var managerUser = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == managerUserId && x.IsActive);

            if (managerUser == null)
            {
                return ServiceResult<IEnumerable<TeamLeaveCalendarDTO>>.Fail("MSG-106", "Access Denied.");
            }

            if (managerUser.EmployeeId == null)
            {
                return ServiceResult<IEnumerable<TeamLeaveCalendarDTO>>.Ok(
                    "MSG-00",
                    "User has no employee profile, so no team calendar is available.",
                    new List<TeamLeaveCalendarDTO>());
            }

            var managerEmployeeId = managerUser.EmployeeId;
            var topLevelFallbackUserId = await _topLevelResolver.GetTopLevelFallbackUserIdAsync();
            var defaultFallbackUserId = await _topLevelResolver.GetDefaultFallbackUserIdAsync();

            var approvedLeaves = await _context.LeaveRequests
                .Include(x => x.Employee)
                    .ThenInclude(e => e.Position)
                .Include(x => x.LeaveType)
                .Where(x =>
                    x.Status == "Approved" &&
                    x.TargetApproverId == managerUserId
                )
                .OrderBy(x => x.StartDate)
                .Select(x => new TeamLeaveCalendarDTO
                {
                    LeaveRequestId = x.LeaveRequestId,
                    EmployeeId = x.EmployeeId,
                    EmployeeName = x.Employee.FirstName + " " + x.Employee.LastName,
                    LeaveTypeId = x.LeaveTypeId,
                    LeaveTypeName = x.LeaveType.LeaveTypeName,
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    NumberOfDays = x.NumberOfDays,
                    Status = x.Status,
                    IsTopLevel = x.Employee.Position != null && x.Employee.Position.IsTopLevel
                })
                .ToListAsync();

            if (!approvedLeaves.Any())
            {
                return ServiceResult<IEnumerable<TeamLeaveCalendarDTO>>.Fail(
                    "MSG-47",
                    "No leaves scheduled.");
            }

            return ServiceResult<IEnumerable<TeamLeaveCalendarDTO>>.Ok(
                "MSG-48",
                "Team leave calendar retrieved successfully.",
                approvedLeaves);
        }

        public async Task<ServiceResult<List<PendingLeaveRequestDTO>>> GetPendingLeaveRequestsAsync(int managerUserId)
        {
            var managerUser = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == managerUserId && x.IsActive);

            if (managerUser == null)
            {
                return ServiceResult<List<PendingLeaveRequestDTO>>.Fail("MSG-106", "Access Denied.");
            }

            var topLevelFallbackUserId = await _topLevelResolver.GetTopLevelFallbackUserIdAsync();
            var defaultFallbackUserId = await _topLevelResolver.GetDefaultFallbackUserIdAsync();

            var pendingRequests = await _context.LeaveRequests
                .Where(lr => lr.Status == "Pending")
                .Include(lr => lr.Employee)
                    .ThenInclude(e => e.Position)
                .Include(lr => lr.LeaveType)
                .Where(lr =>
                    lr.TargetApproverId == managerUserId
                )
                .Select(x => new PendingLeaveRequestDTO
                {
                    LeaveRequestId = x.LeaveRequestId,
                    RequestNumber = x.RequestNumber,
                    EmployeeId = x.EmployeeId,
                    EmployeeName = x.Employee.FullName,
                    LeaveTypeId = x.LeaveTypeId,
                    LeaveTypeName = x.LeaveType.LeaveTypeName,
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    NumberOfDays = x.NumberOfDays,
                    Reason = x.Reason,
                    Status = x.Status,
                    SubmittedDate = x.SubmittedDate,
                    IsTopLevel = x.Employee.Position != null && x.Employee.Position.IsTopLevel
                })
                .OrderByDescending(x => x.SubmittedDate)
                .ToListAsync();

            return ServiceResult<List<PendingLeaveRequestDTO>>.Ok("", "Success", pendingRequests);
        }
    }
}