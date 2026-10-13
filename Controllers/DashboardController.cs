using HRManagement.Services.Employees;
using HRManagement.Authorization;
using HRManagement.DTOs;
using HRManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace HRManagement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly HrmsDbContext _context;

        public DashboardController(HrmsDbContext context)
        {
            _context = context;
        }

        [HttpGet("admin-stats")]
        [RequirePermission("Dashboard.Admin")]
        public async Task<IActionResult> GetAdminStats([FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate)
        {
            var today = DateOnly.FromDateTime(AppTime.Today);
            var start = fromDate ?? today.AddDays(-30);
            var end = toDate ?? today;

            var stats = new StatisticsDto();

            // 1. User Stats
            stats.TotalUsers = await _context.Users.CountAsync();
            stats.NewUsers = await _context.Users.CountAsync(u => DateOnly.FromDateTime(u.CreatedDate) >= start);
            stats.ActiveUsers = await _context.Users.CountAsync(u => u.IsActive == true);

            // 2. Employee & Org Stats
            stats.TotalEmployees = await _context.Employees.CountAsync();
            stats.TotalDepartments = await _context.Departments.CountAsync();

            // 3. Request Stats
            stats.TotalLeaveRequests = await _context.LeaveRequests.CountAsync(r => DateOnly.FromDateTime(r.SubmittedDate) >= start && DateOnly.FromDateTime(r.SubmittedDate) <= end);
            stats.PendingApprovals = await _context.LeaveRequests.CountAsync(r => r.Status == "Pending") +
                                     await _context.OvertimeRequests.CountAsync(r => r.Status == "Pending");

            // 4. Attendance Stats
            var attendanceRecords = await _context.AttendanceRecords
                .Where(a => a.AttendanceDate >= start && a.AttendanceDate <= end)
                .ToListAsync();

            if (attendanceRecords.Any())
            {
                var presentCount = attendanceRecords.Count(a => a.Status == "Present" || a.Status == "Late");
                stats.AttendanceRate = Math.Round((double)presentCount / attendanceRecords.Count * 100, 1);
            }

            stats.OvertimeHours = (double)await _context.OvertimeRequests
                .Where(o => o.Status == "Approved" && o.OvertimeDate >= start && o.OvertimeDate <= end)
                .SumAsync(o => o.TotalHours);

            // 5. System Stats (Real-ish)
            var uptime = AppTime.Now - Process.GetCurrentProcess().StartTime;
            stats.SystemUptime = $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
            
            // Database Size (Mocked for now or query sys tables if possible)
            stats.DatabaseSize = "42.5 MB"; 
            stats.ErrorRate = 0.02; // Mocked
            stats.ApiResponseTime = new Random().Next(30, 150);

            // Recent Activities
            var recentActivities = await _context.AuditLogs
                .Include(a => a.User)
                .OrderByDescending(a => a.ActionDate)
                .Take(5)
                .Select(a => new RecentActivityDto
                {
                    Description = $"{a.Action} on {a.TableName} by {a.User.Username ?? "System"}",
                    Timestamp = a.ActionDate
                })
                .ToListAsync();

            // Alerts: things in this company that need someone's attention (they used to be made-up examples)
            var alerts = await BuildAdminAlertsAsync(today);

            // Scheduled tasks: the background jobs that really run, with how their last run went
            var tasks = ScheduledTasks();

            return Ok(new AdminDashboardDto
            {
                Statistics = stats,
                RecentActivities = recentActivities,
                Alerts = alerts,
                ScheduledTasks = tasks
            });
        }
        private async Task<List<NotificationDto>> BuildAdminAlertsAsync(DateOnly today)
        {
            var staleBefore = AppTime.Now.AddDays(-3);
            var alerts = new List<NotificationDto>();
            void Add(int count, string level, string message)
            {
                if (count > 0) alerts.Add(new NotificationDto { Level = level, Message = message.Replace("{n}", count.ToString()) });
            }

            Add(await _context.LeaveRequests.CountAsync(l => l.Status == "Pending" && l.SubmittedDate < staleBefore),
                "Warning", "{n} đơn nghỉ phép chờ duyệt quá 3 ngày.");
            Add(await _context.OvertimeRequests.CountAsync(o => o.Status == "Pending" && o.SubmittedDate < staleBefore),
                "Warning", "{n} đơn tăng ca chờ duyệt quá 3 ngày.");
            Add(await _context.AttendanceRecords.CountAsync(a => a.ExplanationStatus == "Pending"),
                "Info", "{n} phiếu giải trình chấm công đang chờ duyệt.");
            Add(await _context.EmployeeContracts.CountAsync(c => c.Status == ContractStatuses.Active
                    && c.EndDate.HasValue && c.EndDate >= today && c.EndDate <= today.AddDays(30)),
                "Warning", "{n} hợp đồng lao động hết hạn trong 30 ngày tới.");
            Add(await _context.Employees.CountAsync(e => e.EmploymentStatus == "Active"
                    && !_context.FaceProfiles.Any(f => f.EmployeeId == e.EmployeeId && f.Status == "Active")),
                "Info", "{n} nhân viên chưa đăng ký khuôn mặt để chấm công.");
            Add(await _context.Users.CountAsync(u => !u.IsActive),
                "Info", "{n} tài khoản đang bị khóa.");
            return alerts;
        }

        /// <summary>The Hangfire recurring jobs, with the outcome of their last run.</summary>
        private static List<ScheduledTaskDto> ScheduledTasks()
        {
            var names = new Dictionary<string, string>
            {
                [HRManagement.Services.Backgrounds.HRProcedureBackgroundService.JobId] = "Áp dụng thủ tục nhân sự đến hạn (5 phút/lần)",
                [HRManagement.Services.Backgrounds.PayrollAttendanceReviewService.JobId] = "Mở review chấm công cho kỳ lương (mỗi giờ)",
            };
            try
            {
                using var connection = Hangfire.JobStorage.Current.GetConnection();
                return Hangfire.Storage.StorageConnectionExtensions.GetRecurringJobs(connection)
                    .Select(job => new ScheduledTaskDto
                    {
                        Name = names.GetValueOrDefault(job.Id, job.Id),
                        Status = job.LastJobState switch
                        {
                            "Succeeded" => "Completed",
                            "Processing" => "Running",
                            "Failed" => "Failed",
                            _ => "Pending"
                        }
                    })
                    .ToList();
            }
            catch (InvalidOperationException)
            {
                // No job storage (e.g. Hangfire not configured in this environment)
                return new List<ScheduledTaskDto>();
            }
        }

        [HttpGet("hr-stats")]
        [RequirePermission("Dashboard.Hr")]
        public async Task<IActionResult> GetHrStats([FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate)
        {
            var today = DateOnly.FromDateTime(AppTime.Today);
            var start = fromDate ?? today.AddDays(-30);
            var end = toDate ?? today;

            var stats = new HrStatisticsDto();

            // 1. Basic Counts
            stats.TotalHeadcount = await _context.Employees.CountAsync(e => e.EmploymentStatus == "Active");
            stats.NewHires = await _context.Employees.CountAsync(e => e.JoinDate >= start && e.JoinDate <= end);
            stats.Terminations = await _context.Employees.CountAsync(e => e.ResignationDate >= start && e.ResignationDate <= end);

            // 2. Attendance & Leave
            var attendanceRecords = await _context.AttendanceRecords
                .Where(a => a.AttendanceDate >= start && a.AttendanceDate <= end)
                .ToListAsync();

            if (attendanceRecords.Any())
            {
                var presentCount = attendanceRecords.Count(a => a.Status == "Present" || a.Status == "Late");
                stats.OverallAttendanceRate = Math.Round((double)presentCount / attendanceRecords.Count * 100, 1);
            }

            var approvedLeaves = await _context.LeaveRequests
                .Where(r => r.Status == "Approved" && r.StartDate >= start && r.StartDate <= end)
                .ToListAsync();

            if (stats.TotalHeadcount > 0)
            {
                stats.AverageLeaveDays = Math.Round((double)approvedLeaves.Sum(l => l.NumberOfDays) / stats.TotalHeadcount, 1);
            }

            stats.PendingLeaveRequests = await _context.LeaveRequests.CountAsync(r => r.Status == "Pending");

            // 4. Upcoming Events
            // Probation ends: from active probation contracts; employees without any contract on file fall back
            // to the usual two months after joining
            var probationContracts = await _context.EmployeeContracts
                .Where(c => c.Status == ContractStatuses.Active && c.ContractType == ContractTypes.Probation
                            && c.EndDate.HasValue && c.EndDate >= today && c.EndDate <= today.AddDays(30))
                .Select(c => new UpcomingEventDto
                {
                    EmployeeId = c.EmployeeId,
                    EmployeeName = c.Employee.FullName,
                    Date = c.EndDate!.Value,
                    Detail = ContractTypes.Probation
                })
                .ToListAsync();

            var probationByJoinDate = await _context.Employees
                .Where(e => e.EmploymentStatus == "Active" && !e.EmployeeContracts.Any()
                            && e.JoinDate.AddMonths(2) >= today && e.JoinDate.AddMonths(2) <= today.AddDays(30))
                .Select(e => new UpcomingEventDto
                {
                    EmployeeId = e.EmployeeId,
                    EmployeeName = e.FullName,
                    Date = e.JoinDate.AddMonths(2),
                    Detail = ContractTypes.Probation
                })
                .ToListAsync();

            var upcomingProbation = probationContracts.Concat(probationByJoinDate).OrderBy(e => e.Date).ToList();

            var upcomingRenewals = await _context.EmployeeContracts
                .Where(c => c.Status == ContractStatuses.Active && c.ContractType != ContractTypes.Probation
                            && c.EndDate.HasValue && c.EndDate >= today && c.EndDate <= today.AddDays(30))
                .OrderBy(c => c.EndDate)
                .Select(c => new UpcomingEventDto
                {
                    EmployeeId = c.EmployeeId,
                    EmployeeName = c.Employee.FullName,
                    Date = c.EndDate.Value,
                    Detail = c.ContractType
                })
                .ToListAsync();

            var birthdays = await _context.Employees
                .Where(e => e.DateOfBirth.HasValue && e.DateOfBirth.Value.Month == today.Month)
                .Select(e => new UpcomingEventDto
                {
                    EmployeeId = e.EmployeeId,
                    EmployeeName = e.FullName,
                    Date = e.DateOfBirth.Value,
                    Detail = "Birthday"
                })
                .ToListAsync();

            // Recent Activities
            var recentActivities = await _context.AuditLogs
                .Include(a => a.User)
                .Where(a => a.TableName == "Employees" || a.TableName == "LeaveRequests")
                .OrderByDescending(a => a.ActionDate)
                .Take(5)
                .Select(a => new RecentActivityDto
                {
                    Description = $"{a.Action} on {a.TableName} by {a.User.Username ?? "System"}",
                    Timestamp = a.ActionDate
                })
                .ToListAsync();

            return Ok(new HrDashboardDto
            {
                Statistics = stats,
                UpcomingProbationEnds = upcomingProbation,
                ContractRenewals = upcomingRenewals,
                Birthdays = birthdays,
                RecentHrActivities = recentActivities
            });
        }
        [HttpGet("manager-stats")]
        [RequirePermission("Dashboard.Manager")]
        public async Task<IActionResult> GetManagerStats([FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            var user = await _context.Users.FindAsync(userId);
            if (user?.EmployeeId == null) return NotFound("Chỉ người dùng là quản lý trực tiếp nhân viên mới có dữ liệu đội nhóm.");

            var managerEmployeeId = user.EmployeeId.Value;
            var today = DateOnly.FromDateTime(AppTime.Today);
            var start = fromDate ?? today.AddDays(-30);
            var end = toDate ?? today;

            var result = new ManagerDashboardDto();

            // 1. Team Context
            var teamEmployees = await _context.Employees
                .Where(e => e.ManagerId == managerEmployeeId && e.EmploymentStatus == "Active")
                .ToListAsync();
            var teamEmployeeIds = teamEmployees.Select(e => e.EmployeeId).ToList();

            result.Statistics.TeamSize = teamEmployees.Count;

            // 2. Attendance Stats
            var todayAttendance = await _context.AttendanceRecords
                .Where(a => teamEmployeeIds.Contains(a.EmployeeId) && a.AttendanceDate == today)
                .ToListAsync();

            result.Statistics.PresentToday = todayAttendance.Count(a => a.Status == "Present" || a.Status == "Late");

            var onLeaveToday = await _context.LeaveRequests
                .Where(r => teamEmployeeIds.Contains(r.EmployeeId) && r.Status == "Approved" && r.StartDate <= today && r.EndDate >= today)
                .Select(r => r.EmployeeId)
                .Distinct()
                .CountAsync();
            result.Statistics.OnLeaveToday = onLeaveToday;

            var teamAttendanceHistory = await _context.AttendanceRecords
                .Where(a => teamEmployeeIds.Contains(a.EmployeeId) && a.AttendanceDate >= start && a.AttendanceDate <= end)
                .ToListAsync();

            if (teamAttendanceHistory.Any())
            {
                result.Statistics.TeamAttendanceRate = Math.Round((double)teamAttendanceHistory.Count(a => a.Status == "Present" || a.Status == "Late") / teamAttendanceHistory.Count * 100, 1);
            }

            // 3. Pending Actions
            result.PendingLeaveRequests = await _context.LeaveRequests
                .Where(r => teamEmployeeIds.Contains(r.EmployeeId) && r.Status == "Pending")
                .Select(r => new PendingRequestDto
                {
                    RequestId = r.LeaveRequestId,
                    EmployeeName = r.Employee.FullName,
                    LeaveType = r.LeaveType.LeaveTypeName,
                    Days = r.NumberOfDays
                })
                .ToListAsync();

            result.UpcomingTeamLeaves = await _context.LeaveRequests
                .Where(r => teamEmployeeIds.Contains(r.EmployeeId) && r.Status == "Approved" && r.StartDate > today && r.StartDate <= today.AddDays(14))
                .Select(r => new UpcomingLeaveDto
                {
                    EmployeeName = r.Employee.FullName,
                    LeaveType = r.LeaveType.LeaveTypeName,
                    DateRange = $"{r.StartDate:dd/MM} - {r.EndDate:dd/MM}"
                })
                .ToListAsync();

            // 4. Tasks & Performance
            var teamUserIds = await _context.Users.Where(u => u.EmployeeId.HasValue && teamEmployeeIds.Contains(u.EmployeeId.Value)).Select(u => u.UserId).ToListAsync();
            var teamTasks = await _context.Tasks
                .Where(t => teamUserIds.Contains(t.AssignedTo))
                .ToListAsync();

            result.TaskPerformance.ActiveTasks = teamTasks.Count(t => t.Status == "Pending" || t.Status == "InProgress");
            result.TaskPerformance.OverdueTasks = teamTasks.Count(t => t.Status != "Completed" && t.DueDate.HasValue && t.DueDate < today);
            
            if (teamTasks.Any())
            {
                result.TaskPerformance.CompletionRate = Math.Round((double)teamTasks.Count(t => t.Status == "Completed") / teamTasks.Count * 100, 1);
            }

            // 5. Action Summary
            result.ActionSummary.PendingLeaveApprovals = result.PendingLeaveRequests.Count;
            result.ActionSummary.PendingOvertimeApprovals = await _context.OvertimeRequests.CountAsync(r => teamEmployeeIds.Contains(r.EmployeeId) && r.Status == "Pending");
            result.ActionSummary.PendingAttendanceCorrections = await _context.AttendanceRecords.CountAsync(a => teamEmployeeIds.Contains(a.EmployeeId) && a.ExplanationStatus == "Pending");
            result.ActionSummary.TotalPendingApprovals = result.ActionSummary.PendingLeaveApprovals + result.ActionSummary.PendingOvertimeApprovals + result.ActionSummary.PendingAttendanceCorrections;

            // 6. Insights & Milestones
            result.TeamInsights.Add(new TeamInsightDto { Label = "Availability", Value = $"{100 - (result.Statistics.TeamSize > 0 ? (onLeaveToday * 100 / result.Statistics.TeamSize) : 0)}%" });
            
            result.TeamMilestones = await _context.Employees
                .Where(e => teamEmployeeIds.Contains(e.EmployeeId) && e.DateOfBirth.HasValue && e.DateOfBirth.Value.Month == today.Month)
                .Select(e => new UpcomingEventDto
                {
                    EmployeeId = e.EmployeeId,
                    EmployeeName = e.FullName,
                    Date = e.DateOfBirth.Value,
                    Detail = "Birthday"
                })
                .ToListAsync();

            return Ok(result);
        }
    }
}
