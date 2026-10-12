using HRManagement.DTOs.LeaveRequest;
using HRManagement.DTOs.OvertimeRequest;
using HRManagement.Models;
using Microsoft.EntityFrameworkCore;
using HRManagement.Services.Approvals;
using System.Text.Json;

namespace HRManagement.Services.Overtimes
{
    public class OvertimeRequestService : IOvertimeRequestService
    {
        private readonly HrmsDbContext _context;
        private readonly IApprovalRouteService _approvalRouteService;
        private readonly ITopLevelResolver _topLevelResolver;

        public OvertimeRequestService(
            HrmsDbContext context, 
            IApprovalRouteService approvalRouteService,
            ITopLevelResolver topLevelResolver)
        {
            _context = context;
            _approvalRouteService = approvalRouteService;
            _topLevelResolver = topLevelResolver;
        }

        public async Task<ServiceResult<string>> CreateOvertimeRequestAsync(int userId, CreateOvertimeRequestDTO dto)
        {
            var today = DateOnly.FromDateTime(AppTime.Now);

            // 1. Validate date range: -3 to +30 days
            if (dto.OvertimeDate < today.AddDays(-3))
            {
                return ServiceResult<string>.Fail("MSG-OT-03", "Cannot create request for dates older than 3 days.");
            }
            if (dto.OvertimeDate > today.AddDays(30))
            {
                return ServiceResult<string>.Fail("MSG-OT-04", "Cannot create request for dates further than 30 days in the future.");
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);

            if (user == null || user.EmployeeId == null)
            {
                return ServiceResult<string>.Fail("MSG-106", "Access Denied.");
            }

            var employee = await _context.Employees
                .FirstOrDefaultAsync(x => x.EmployeeId == user.EmployeeId);

            if (employee == null)
            {
                return ServiceResult<string>.Fail("MSG-104", "Employee not found.");
            }

            // Check employee status
            if (employee.EmploymentStatus != "Active")
            {
                return ServiceResult<string>.Fail("MSG-EMP-01", "Employee is not active.");
            }

            // Check join date
            if (employee.JoinDate > dto.OvertimeDate)
            {
                return ServiceResult<string>.Fail("MSG-EMP-02", "Cannot create overtime request before join date.");
            }

            // 2. Fetch shift assignment for that date to determine OTType and Shift hours
            var shiftAssignment = await _context.ShiftAssignments
                .Include(x => x.Shift)
                .FirstOrDefaultAsync(x => 
                x.EmployeeId == employee.EmployeeId && 
                x.AssignmentDate == dto.OvertimeDate &&
                x.Status == "Active");

            string otType = shiftAssignment != null ? "NormalDay" : "DayOff";
            // Note: Holiday detection would go here if Holiday table existed.

            TimeOnly actualStartTime;
            TimeOnly actualEndTime;

            // 3. Resolve StartTime and EndTime based on OTMode
            if (otType == "NormalDay")
            {
                var shift = shiftAssignment!.Shift;
                if (dto.OTMode == "AfterShift")
                {
                    if (!dto.EndTime.HasValue)
                        return ServiceResult<string>.Fail("MSG-OT-05", "End time is required for AfterShift mode.");
                    
                    actualStartTime = shift.EndTime;
                    actualEndTime = dto.EndTime.Value;
                }
                else if (dto.OTMode == "BeforeShift")
                {
                    if (!dto.StartTime.HasValue)
                        return ServiceResult<string>.Fail("MSG-OT-06", "Start time is required for BeforeShift mode.");

                    actualStartTime = dto.StartTime.Value;
                    actualEndTime = shift.StartTime;
                }
                else // FullRange or others during a normal day - usually not recommended by UI but allowed by logic
                {
                    if (!dto.StartTime.HasValue || !dto.EndTime.HasValue)
                        return ServiceResult<string>.Fail("MSG-OT-07", "Both times are required for FullRange mode.");
                    
                    actualStartTime = dto.StartTime.Value;
                    actualEndTime = dto.EndTime.Value;
                }
            }
            else // DayOff
            {
                if (!dto.StartTime.HasValue || !dto.EndTime.HasValue)
                    return ServiceResult<string>.Fail("MSG-OT-07", "Both times are required for DayOff.");

                actualStartTime = dto.StartTime.Value;
                actualEndTime = dto.EndTime.Value;
            }

            // 4. Calculate hours and basic validation
            var startTs = actualStartTime.ToTimeSpan();
            var endTs = actualEndTime.ToTimeSpan();

            // Handle overnight case
            if (endTs <= startTs)
            {
                endTs = endTs.Add(TimeSpan.FromDays(1));
            }

            var totalHours = (decimal)(endTs - startTs).TotalHours;

            if (totalHours < 0.5m)
            {
                return ServiceResult<string>.Fail("MSG-OT-08", "Minimum overtime is 30 minutes.");
            }

            if (otType == "NormalDay" && totalHours > 4)
            {
                return ServiceResult<string>.Fail("MSG-OT-09", "Overtime on normal workdays cannot exceed 4 hours.");
            }

            if (totalHours > 12)
            {
                return ServiceResult<string>.Fail("MSG-OT-10", "Overtime cannot exceed 12 hours.");
            }

            // Check if total work + OT > 12 hours on normal days
            if (otType == "NormalDay" && shiftAssignment?.Shift != null)
            {
                var shiftDuration = (decimal)shiftAssignment.Shift.WorkingHours;
                if (shiftDuration + totalHours > 12)
                {
                    return ServiceResult<string>.Fail("MSG-OT-11", $"Total working hours ({shiftDuration + totalHours}h) cannot exceed 12 hours including overtime.");
                }
            }

            // 5. Check for overlapping requests
            var hasOverlap = await _context.OvertimeRequests
                .Where(x => x.EmployeeId == employee.EmployeeId && x.OvertimeDate == dto.OvertimeDate && x.Status != "Rejected" && x.Status != "Cancelled")
                .AnyAsync(x => (actualStartTime < x.EndTime && actualEndTime > x.StartTime));

            if (hasOverlap)
            {
                return ServiceResult<string>.Fail("MSG-OT-02", "This request overlaps with an existing overtime request.");
            }

            // 6. Check for overlap with shift hours
            if (otType == "NormalDay")
            {
                var shift = shiftAssignment!.Shift;
                var baseDate = dto.OvertimeDate.ToDateTime(TimeOnly.MinValue);
                
                DateTime shiftStartDt = baseDate.Add(shift.StartTime.ToTimeSpan());
                DateTime shiftEndDt = baseDate.Add(shift.EndTime.ToTimeSpan());
                DateTime otStartDt = baseDate.Add(actualStartTime.ToTimeSpan());
                DateTime otEndDt = baseDate.Add(actualEndTime.ToTimeSpan());

                if (shift.IsOvernight == true) shiftEndDt = shiftEndDt.AddDays(1);
                if (actualEndTime <= actualStartTime) otEndDt = otEndDt.AddDays(1);

                if (otStartDt < shiftEndDt && otEndDt > shiftStartDt)
                {
                    return ServiceResult<string>.Fail("MSG-OT-01", "Overtime must be outside working hours.");
                }
            }

            var authResult = await _approvalRouteService.CanSubmitRequestAsync(employee.EmployeeId);
            if (!authResult.IsAuthorized)
            {
                return ServiceResult<string>.Fail("MSG-41", authResult.Message ?? "Invalid approval route.");
            }

            var approverId = await _approvalRouteService.GetApproverIdAsync(employee.EmployeeId);
            string initialStatus = approverId.HasValue ? "Pending" : "Approved";

            string requestNumber = $"OT-{AppTime.Now:yyyyMMddHHmmss}";

            var overtimeRequest = new OvertimeRequest
            {
                RequestNumber = requestNumber,
                EmployeeId = employee.EmployeeId,
                OvertimeDate = dto.OvertimeDate,
                StartTime = actualStartTime,
                EndTime = actualEndTime,
                TotalHours = totalHours,
                Reason = dto.Reason,
                TaskDescription = dto.TaskDescription,
                Status = initialStatus,
                TargetApproverId = approverId,
                SubmittedDate = AppTime.Now,
                ReviewedDate = initialStatus == "Approved" ? AppTime.Now : null,
                ReviewedBy = null,
                ApprovedDate = initialStatus == "Approved" ? AppTime.Now : null,
                ApprovedBy = null // System
            };

            _context.OvertimeRequests.Add(overtimeRequest);

            if (approverId.HasValue)
            {
                var notification = new Notification
                {
                    RecipientUserId = approverId.Value,
                    NotificationType = "Overtime",
                    Title = "New Overtime Request",
                    Message = $"A new overtime request {requestNumber} is waiting for approval.",
                    RelatedEntity = "OvertimeRequest",
                    RelatedEntityId = overtimeRequest.OvertimeRequestId,
                    IsRead = false,
                    SentDate = AppTime.Now
                };
                _context.Notifications.Add(notification);
            }

            var auditLog = new AuditLog
            {
                TableName = "OvertimeRequests",
                Action = "INSERT",
                RecordId = overtimeRequest.OvertimeRequestId,
                UserId = userId,
                NewValues = JsonSerializer.Serialize(new
                {
                    overtimeRequest.OvertimeRequestId,
                    overtimeRequest.EmployeeId,
                    overtimeRequest.OvertimeDate,
                    overtimeRequest.StartTime,
                    overtimeRequest.EndTime,
                    overtimeRequest.TotalHours,
                    overtimeRequest.Status
                }),
                ActionDate = AppTime.Now
            };

            _context.AuditLogs.Add(auditLog);

            await _context.SaveChangesAsync();

            return ServiceResult<string>.Ok("MSG-29", "Overtime request submitted successfully.", null);
        }
        public async Task<ServiceResult<string>> ApproveOvertimeRequestAsync(int managerUserId, int requestId, ApproveOvertimeRequestDTO dto)
        {
            var manager = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == managerUserId && x.IsActive);

            if (manager == null)
                return ServiceResult<string>.Fail("MSG-41", "Manager has no approval authority.");

            var request = await _context.OvertimeRequests
                .Include(x => x.Employee) // Needed for notification
                .FirstOrDefaultAsync(x => x.OvertimeRequestId == requestId);

            if (request == null || request.Status != "Pending")
                return ServiceResult<string>.Fail("MSG-42", "Request already processed or not found.");

            // Validation using frozen TargetApproverId
            if (request.TargetApproverId == null || request.TargetApproverId != managerUserId)
            {
                return ServiceResult<string>.Fail("MSG-41", "You do not have authority to process this request.");
            }

            request.Status = "Approved";
            request.ReviewedDate = AppTime.Now;
            request.ReviewedBy = managerUserId;
            request.ApprovedDate = AppTime.Now;
            request.ApprovedBy = managerUserId;

            // Notification for employee
            var employeeUser = await _context.Users.FirstOrDefaultAsync(u => u.EmployeeId == request.EmployeeId);
            if (employeeUser != null)
            {
                var notification = new Notification
                {
                    RecipientUserId = employeeUser.UserId,
                    NotificationType = "Overtime",
                    Title = "Overtime Approved",
                    Message = $"Your overtime request {request.RequestNumber} for {request.OvertimeDate} has been approved.",
                    RelatedEntity = "OvertimeRequest",
                    RelatedEntityId = request.OvertimeRequestId,
                    IsRead = false,
                    SentDate = AppTime.Now
                };
                _context.Notifications.Add(notification);
            }

            var auditLog = new AuditLog
            {
                TableName = "OvertimeRequests",
                Action = "UPDATE",
                RecordId = request.OvertimeRequestId,
                UserId = managerUserId,
                NewValues = JsonSerializer.Serialize(new
                {
                    request.Status,
                    request.ApprovedBy,
                    request.ApprovedDate
                }),
                ActionDate = AppTime.Now
            };

            _context.AuditLogs.Add(auditLog);

            await _context.SaveChangesAsync();

            return ServiceResult<string>.Ok("MSG-40", "Overtime request approved.", null);
        }
        public async Task<ServiceResult<string>> RejectOvertimeRequestAsync(int managerUserId, int requestId, RejectOvertimeRequestDTO dto)
        {
            var manager = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == managerUserId && x.IsActive);

            if (manager == null)
                return ServiceResult<string>.Fail("MSG-41", "Manager has no approval authority.");

            var request = await _context.OvertimeRequests
                .Include(x => x.Employee)
                .FirstOrDefaultAsync(x => x.OvertimeRequestId == requestId);

            if (request == null || request.Status != "Pending")
                return ServiceResult<string>.Fail("MSG-42", "Request already processed or not found.");

            // Validation using frozen TargetApproverId
            if (request.TargetApproverId == null || request.TargetApproverId != managerUserId)
            {
                return ServiceResult<string>.Fail("MSG-41", "You do not have authority to process this request.");
            }

            request.Status = "Rejected";
            request.ReviewedDate = AppTime.Now;
            request.ReviewedBy = managerUserId;
            request.RejectionReason = dto.Reason;

            // Notification for employee
            var employeeUser = await _context.Users.FirstOrDefaultAsync(u => u.EmployeeId == request.EmployeeId);
            if (employeeUser != null)
            {
                var notification = new Notification
                {
                    RecipientUserId = employeeUser.UserId,
                    NotificationType = "Overtime",
                    Title = "Overtime Rejected",
                    Message = $"Your overtime request {request.RequestNumber} for {request.OvertimeDate} has been rejected. Reason: {dto.Reason}",
                    RelatedEntity = "OvertimeRequest",
                    RelatedEntityId = request.OvertimeRequestId,
                    IsRead = false,
                    SentDate = AppTime.Now
                };
                _context.Notifications.Add(notification);
            }

            var auditLog = new AuditLog
            {
                TableName = "OvertimeRequests",
                Action = "UPDATE",
                RecordId = request.OvertimeRequestId,
                UserId = managerUserId,
                NewValues = JsonSerializer.Serialize(new
                {
                    request.Status,
                    request.RejectionReason
                }),
                ActionDate = AppTime.Now
            };

            _context.AuditLogs.Add(auditLog);

            await _context.SaveChangesAsync();

            return ServiceResult<string>.Ok("MSG-44", "Overtime request rejected.", null);
        }
        // cancel request
        public async Task<ServiceResult<string>> CancelOvertimeRequestAsync(int userId, int requestId, CancelOvertimeRequestDTO dto)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);

            if (user == null || user.EmployeeId == null)
                return ServiceResult<string>.Fail("MSG-106", "Access denied.");

            var request = await _context.OvertimeRequests
                .FirstOrDefaultAsync(x => x.OvertimeRequestId == requestId);

            if (request == null)
                return ServiceResult<string>.Fail("MSG-104", "Request not found.");

            if (request.EmployeeId != user.EmployeeId)
                return ServiceResult<string>.Fail("MSG-106", "Access denied.");

            if (request.Status != "Pending")
                return ServiceResult<string>.Fail("MSG-38", "Cannot cancel processed request.");

            request.Status = "Cancelled";
            request.RejectionReason = dto.Reason;

            var auditLog = new AuditLog
            {
                TableName = "OvertimeRequests",
                Action = "UPDATE",
                RecordId = request.OvertimeRequestId,
                UserId = userId,
                NewValues = JsonSerializer.Serialize(new
                {
                    request.Status,
                    request.RejectionReason
                }),
                ActionDate = AppTime.Now
            };

            _context.AuditLogs.Add(auditLog);

            await _context.SaveChangesAsync();

            return ServiceResult<string>.Ok("MSG-37", "Overtime request cancelled successfully.", null);
        }
        // get my request
        public async Task<ServiceResult<List<MyOvertimeRequestDTO>>> GetMyOvertimeRequestsAsync(int userId)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);

            if (user == null || user.EmployeeId == null)
                return ServiceResult<List<MyOvertimeRequestDTO>>.Ok(
                    "MSG-00", 
                    "User has no employee profile, so they have no personal overtime requests.", 
                    new List<MyOvertimeRequestDTO>()
                );

            var requests = await _context.OvertimeRequests
                .Where(x => x.EmployeeId == user.EmployeeId)
                .OrderByDescending(x => x.SubmittedDate)
                .ToListAsync();

            var assignments = await _context.ShiftAssignments
                .Include(x => x.Shift)
                .Where(x => x.EmployeeId == user.EmployeeId && x.Status == "Active")
                .ToListAsync();

            var result = requests.Select(x => {
                var sa = assignments.FirstOrDefault(a => a.AssignmentDate == x.OvertimeDate);
                string otType = sa != null ? "NormalDay" : "DayOff";
                string otMode = "FullRange";
                if (sa != null)
                {
                    if (x.StartTime == sa.Shift.EndTime) otMode = "AfterShift";
                    else if (x.EndTime == sa.Shift.StartTime) otMode = "BeforeShift";
                }

                return new MyOvertimeRequestDTO
                {
                    OvertimeRequestId = x.OvertimeRequestId,
                    RequestNumber = x.RequestNumber,
                    OvertimeDate = x.OvertimeDate,
                    StartTime = x.StartTime,
                    EndTime = x.EndTime,
                    TotalHours = x.TotalHours,
                    OTType = otType,
                    OTMode = otMode,
                    Reason = x.Reason,
                    Status = x.Status,
                    SubmittedDate = x.SubmittedDate
                };
            }).ToList();

            return ServiceResult<List<MyOvertimeRequestDTO>>.Ok("", "Success", result);
        }
        public async Task<ServiceResult<List<PendingOvertimeRequestDTO>>> GetPendingOvertimeRequestsAsync(int managerUserId)
        {
            var managerUser = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == managerUserId && x.IsActive);

            if (managerUser == null)
            {
                return ServiceResult<List<PendingOvertimeRequestDTO>>.Fail("MSG-106", "Access Denied.");
            }

            var topLevelFallbackUserId = await _topLevelResolver.GetTopLevelFallbackUserIdAsync();
            var defaultFallbackUserId = await _topLevelResolver.GetDefaultFallbackUserIdAsync();

            var pendingRequests = await _context.OvertimeRequests
                .Where(or => or.Status == "Pending")
                .Include(or => or.Employee)
                    .ThenInclude(e => e.Position)
                .Where(or =>
                    or.TargetApproverId == managerUserId
                )
                .OrderByDescending(x => x.SubmittedDate)
                .ToListAsync();

            // Fetch shift assignments to determine OTType/Mode
            var employeeIds = pendingRequests.Select(x => x.EmployeeId).Distinct().ToList();
            var dates = pendingRequests.Select(x => x.OvertimeDate).Distinct().ToList();

            var assignments = await _context.ShiftAssignments
                .Include(x => x.Shift)
                .Where(x => employeeIds.Contains(x.EmployeeId) && dates.Contains(x.AssignmentDate) && x.Status == "Active")
                .ToListAsync();

            var result = pendingRequests.Select(x => {
                var sa = assignments.FirstOrDefault(a => a.EmployeeId == x.EmployeeId && a.AssignmentDate == x.OvertimeDate);
                string otType = sa != null ? "NormalDay" : "DayOff";
                string otMode = "FullRange";
                if (sa != null)
                {
                    if (x.StartTime == sa.Shift.EndTime) otMode = "AfterShift";
                    else if (x.EndTime == sa.Shift.StartTime) otMode = "BeforeShift";
                }

                return new PendingOvertimeRequestDTO
                {
                    OvertimeRequestId = x.OvertimeRequestId,
                    RequestNumber = x.RequestNumber,
                    EmployeeId = x.EmployeeId,
                    EmployeeName = x.Employee.FullName,
                    OvertimeDate = x.OvertimeDate,
                    StartTime = x.StartTime,
                    EndTime = x.EndTime,
                    TotalHours = x.TotalHours,
                    OTType = otType,
                    OTMode = otMode,
                    Reason = x.Reason,
                    TaskDescription = x.TaskDescription,
                    Status = x.Status,
                    SubmittedDate = x.SubmittedDate,
                    IsTopLevel = x.Employee.Position != null && x.Employee.Position.IsTopLevel
                };
            }).ToList();

            return ServiceResult<List<PendingOvertimeRequestDTO>>.Ok("", "Success", result);
        }
    }
}