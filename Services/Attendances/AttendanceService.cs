using AutoMapper;
using HRManagement.DataAcess.Interfaces;
using HRManagement.DTOs;
using HRManagement.DTOs.Attendances;
using HRManagement.Models;
using HRManagement.Services.FaceVerifications;
using HRManagement.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Services.Attendances
{
    public class AttendanceService : IAttendanceService
    {
        private readonly IAttendanceRepository _attendanceRepository;
        private readonly IFaceVerificationService _faceVerificationService;
        private readonly HrmsDbContext _context;
        private readonly IMapper _mapper;
        private readonly INotificationService _notifications;

        public AttendanceService(IAttendanceRepository attendanceRepository, IFaceVerificationService faceVerificationService, HrmsDbContext context, IMapper mapper, INotificationService notifications)
        {
            _attendanceRepository = attendanceRepository;
            _faceVerificationService = faceVerificationService;
            _context = context;
            _mapper = mapper;
            _notifications = notifications;
        }

        // The employee's direct manager reviews explanations
        private async Task NotifyExplanationSubmittedAsync(AttendanceRecord attendance)
        {
            if (await _notifications.GetManagerUserIdAsync(attendance.EmployeeId) is not { } managerUserId)
                return;

            var name = await _context.Employees.Where(e => e.EmployeeId == attendance.EmployeeId).Select(e => e.FullName).FirstOrDefaultAsync();
            _notifications.Notify(managerUserId, NotificationTypes.ExplanationApproval, "Phiếu giải trình mới cần duyệt",
                $"{name} gửi giải trình chấm công ngày {attendance.AttendanceDate:dd/MM/yyyy}.",
                "AttendanceRecord", attendance.AttendanceId);
            await _context.SaveChangesAsync();
        }
        public async Task<AttendanceResponseDto> CheckInAsync(int employeeId, CheckInRequestDto dto)
        {
            var now = AppTime.Now;
            var today = DateOnly.FromDateTime(now);

            var assignment = await _attendanceRepository.GetActiveShiftAssignmentAsync(employeeId, today);
            if (assignment == null)
                throw new InvalidOperationException("Bạn chưa được phân ca làm việc hôm nay.");

            var shift = assignment.Shift;
            if (shift == null)
                throw new InvalidOperationException("Không tìm thấy ca làm việc.");

            var attendance = await _attendanceRepository.GetAttendanceByEmployeeAndDateAsync(employeeId, today);

            if (attendance != null && (attendance.IsLocked ?? false))
                throw new InvalidOperationException("Bản ghi chấm công đã bị khóa.");

            if (attendance != null && attendance.CheckInTime.HasValue)
                throw new InvalidOperationException("Bạn đã check-in hôm nay rồi.");

            // Fetch approved OT to expand time windows
            var approvedOts = await _context.OvertimeRequests
                .Where(o => o.EmployeeId == employeeId && o.OvertimeDate == today && o.Status == "Approved")
                .ToListAsync();

            var (earliestCheckIn, latestCheckIn) = AttendanceHelper.CalculateCheckInWindow(shift, today, approvedOts);

            if (now < earliestCheckIn)
                throw new InvalidOperationException($"Chưa đến thời gian check-in. Bạn chỉ được check-in từ {earliestCheckIn:HH:mm}.");

            if (now > latestCheckIn)
                throw new InvalidOperationException("Đã quá thời gian check-in cho phép.");

            if (string.IsNullOrWhiteSpace(dto.FaceImageBase64))
                throw new ArgumentException("Vui lòng chụp ảnh khuôn mặt để check-in.");

            // STEP 1: Verify face trước
            var faceResult = await _faceVerificationService.VerifyAsync(
                employeeId,
                dto.FaceImageBase64,
                "CheckIn",
                dto.DeviceInfo,
                dto.IpAddress,
                dto.Location
            );

            if (!faceResult.IsMatch)
                throw new InvalidOperationException($"Xác minh khuôn mặt thất bại. {faceResult.FailureReason}");

            var shiftStart = today.ToDateTime(shift.StartTime);
            int lateMinutes = 0;
            var lateThreshold = shiftStart.AddMinutes(shift.LateGraceMinutes ?? 5);

            if (now > lateThreshold)
            {
                lateMinutes = (int)Math.Floor((now - shiftStart).TotalMinutes);
            }

            var log = new AttendanceLog
            {
                EmployeeId = employeeId,
                ShiftId = shift.ShiftId,
                LogTime = now,
                LogType = "CheckIn",
                Source = "Web",
                DeviceInfo = dto.DeviceInfo,
                IpAddress = dto.IpAddress,
                Location = dto.Location,
                Remarks = dto.Remarks,
                IsValid = true,
                VerificationMethod = "Face",
                VerificationStatus = "Verified",
                CreatedDate = now,
                CreatedBy = employeeId
            };

            await _attendanceRepository.AddAttendanceLogAsync(log);

            if (attendance == null)
            {
                bool validLoc = await AttendanceHelper.IsValidCheckIn(_context, dto);
                attendance = new AttendanceRecord
                {
                    EmployeeId = employeeId,
                    AttendanceDate = today,
                    ShiftId = shift.ShiftId,
                    CheckInTime = now,
                    CheckOutTime = null,
                    WorkingHours = null,
                    OvertimeHours = 0,
                    LateMinutes = lateMinutes,
                    EarlyLeaveMinutes = 0,
                    Status = lateMinutes > 0 ? "Late" : "Present",
                    ExplanationStatus = validLoc ? null : "Required",
                    Source = "Web",
                    IsManualAdjusted = false,
                    IsLocked = false,
                    ApprovedBy = null,
                    ApprovedDate = null,
                    Location = dto.Location,
                    Remarks = dto.Remarks,
                    CreatedDate = now,
                    ModifiedDate = null,
                    ModifiedBy = null,
                    CheckInVerificationMethod = "Face",
                    CheckInVerified = true

                };

                await _attendanceRepository.AddAttendanceAsync(attendance);
            }
            else
            {
                bool validLoc = await AttendanceHelper.IsValidCheckIn(_context, dto);
                attendance.ShiftId = shift.ShiftId;
                attendance.CheckInTime = now;
                attendance.LateMinutes = lateMinutes;
                attendance.Status = lateMinutes > 0 ? "Late" : "Present";
                if (!validLoc) attendance.ExplanationStatus = "Required";
                attendance.Source = "Web";
                attendance.Location = dto.Location;
                attendance.Remarks = dto.Remarks;
                attendance.ModifiedDate = now;
                attendance.ModifiedBy = employeeId;
                attendance.CheckInVerificationMethod = "Face";
                attendance.CheckInVerified = true;

                await _attendanceRepository.UpdateAttendanceAsync(attendance);
            }

            try
            {
                await _attendanceRepository.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                var existing = await _attendanceRepository.GetAttendanceByEmployeeAndDateAsync(employeeId, today);
                if (existing != null)
                {
                    return _mapper.Map<AttendanceResponseDto>(existing);
                }
                throw;
            }

            var result = await _attendanceRepository.GetAttendanceByEmployeeAndDateAsync(employeeId, today)
                ?? throw new InvalidOperationException("Không tìm thấy bản ghi chấm công sau khi check-in.");

            return _mapper.Map<AttendanceResponseDto>(result);
        }

        public async Task<AttendanceResponseDto> CheckOutAsync(int employeeId, CheckOutRequestDto dto)
        {
            var now = AppTime.Now;

            // Lấy bản ghi chấm công gần nhất (có thể đã checkout rồi - cho phép checkout lại)
            var attendance = await _attendanceRepository.GetLatestAttendanceRecordAsync(employeeId);

            if (attendance == null || !attendance.CheckInTime.HasValue)
                throw new InvalidOperationException("Bạn chưa check-in hoặc không tìm thấy bản ghi chấm công.");

            if (attendance.IsLocked ?? false)
                throw new InvalidOperationException("Bản ghi chấm công đã bị khóa.");

            if (string.IsNullOrWhiteSpace(dto.FaceImageBase64))
                throw new ArgumentException("Vui lòng chụp ảnh khuôn mặt để check-out.");

            Shift? shift = null;
            if (attendance.ShiftId.HasValue)
            {
                shift = await _attendanceRepository.GetShiftByIdAsync(attendance.ShiftId.Value);
            }

            // VALIDATE khung giờ check-out
            if (shift != null)
            {
                var attendanceDate = attendance.AttendanceDate;
                // Fetch approved OT to expand time windows
                var approvedOts = await _context.OvertimeRequests
                    .Where(o => o.EmployeeId == employeeId && o.OvertimeDate == attendanceDate && o.Status == "Approved")
                    .ToListAsync();

                var (earliestCheckOut, latestCheckOut) = AttendanceHelper.CalculateCheckOutWindow(shift, attendanceDate, approvedOts);

                if (now < earliestCheckOut)
                    throw new InvalidOperationException($"Chưa đến thời gian check-out. Bạn chỉ được check-out từ {earliestCheckOut:HH:mm}.");

                if (now > latestCheckOut)
                    throw new InvalidOperationException("Đã quá thời gian check-out cho phép.");
            }

            // STEP 1: Verify face trước
            var faceResult = await _faceVerificationService.VerifyAsync(
                employeeId,
                dto.FaceImageBase64,
                "CheckOut",
                dto.DeviceInfo,
                dto.IpAddress,
                dto.Location
            );

            if (!faceResult.IsMatch)
                throw new InvalidOperationException($"Xác minh khuôn mặt thất bại. {faceResult.FailureReason}");

            var log = new AttendanceLog
            {
                EmployeeId = employeeId,
                ShiftId = attendance.ShiftId,
                LogTime = now,
                LogType = "CheckOut",
                Source = "Web",
                DeviceInfo = dto.DeviceInfo,
                IpAddress = dto.IpAddress,
                Location = dto.Location,
                Remarks = dto.Remarks,
                IsValid = true,
                VerificationMethod = "Face",
                VerificationStatus = "Verified",
                CreatedDate = now,
                CreatedBy = employeeId
            };

            await _attendanceRepository.AddAttendanceLogAsync(log);

            attendance.CheckOutTime = now;
            attendance.Location = dto.Location ?? attendance.Location;
            attendance.Remarks = dto.Remarks ?? attendance.Remarks;
            attendance.ModifiedDate = now;
            attendance.ModifiedBy = employeeId;
            attendance.CheckOutVerificationMethod = "Face";
            attendance.CheckOutVerified = true;

            bool validLocOut = await AttendanceHelper.IsValidCheckIn(_context, dto);
            if (!validLocOut) attendance.ExplanationStatus = "Required";

            AttendanceHelper.RecalculateAttendanceSummary(attendance, shift);

            attendance.Source = "Web-Face";

            // Entity đã được track bởi EF Core từ GetOpenAttendanceRecordAsync,
            // không cần gọi Update() vì sẽ mark cả navigation properties → lỗi.
            await _attendanceRepository.SaveChangesAsync();

            var result = await _attendanceRepository.GetAttendanceByIdAsync(attendance.AttendanceId)
                ?? throw new InvalidOperationException("Không tìm thấy bản ghi chấm công sau khi check-out.");

            return _mapper.Map<AttendanceResponseDto>(result);
        }

        public async Task<AttendanceDetailResponseDto?> GetMyTodayAsync(int employeeId)
        {
            var today = DateOnly.FromDateTime(AppTime.Now);
            var attendance = await _attendanceRepository.GetAttendanceByEmployeeAndDateAsync(employeeId, today);

            if (attendance == null)
                return null;

            var logs = await _attendanceRepository.GetLogsByEmployeeAndDateAsync(employeeId, today);

            return new AttendanceDetailResponseDto
            {
                Attendance = _mapper.Map<AttendanceResponseDto>(attendance),
                Logs = logs.Select(l => _mapper.Map<AttendanceLogResponseDto>(l)).ToList()
            };
        }

        public async Task<List<AttendanceResponseDto>> GetMyHistoryAsync(int employeeId, DateOnly? fromDate, DateOnly? toDate)
        {
            return await SearchAttendanceWithLeavesAsync(fromDate, toDate, employeeId, null);
        }

        public async Task<List<AttendanceResponseDto>> GetAttendanceByDateAsync(DateOnly date)
        {
            return await SearchAttendanceWithLeavesAsync(date, date, null, null);
        }

        public async Task<List<AttendanceResponseDto>> SearchAttendanceAsync(DateOnly? fromDate, DateOnly? toDate, int? employeeId, string? status)
        {
            return await SearchAttendanceWithLeavesAsync(fromDate, toDate, employeeId, status);
        }

        public async Task<PagedResultDto<AttendanceResponseDto>> SearchAttendancePageAsync(
            DateOnly? fromDate, DateOnly? toDate, int? employeeId, string? status, string? explanationStatus,
            int? managerEmployeeId, int page, int pageSize)
        {
            var result = new PagedResultDto<AttendanceResponseDto> { Page = page, PageSize = pageSize };

            // A manager only sees their direct reports (the client used to download everyone and hide the rest)
            HashSet<int>? team = null;
            if (managerEmployeeId.HasValue)
            {
                team = (await _context.Employees.AsNoTracking()
                        .Where(e => e.ManagerId == managerEmployeeId.Value)
                        .Select(e => e.EmployeeId)
                        .ToListAsync())
                    .ToHashSet();
                if (employeeId.HasValue && !team.Contains(employeeId.Value))
                    return result;
            }

            // Leave and absence rows are built in memory from several tables, so the page is cut after merging
            IEnumerable<AttendanceResponseDto> rows = await SearchAttendanceWithLeavesAsync(fromDate, toDate, employeeId, status);
            if (team != null)
                rows = rows.Where(r => team.Contains(r.EmployeeId));
            if (!string.IsNullOrWhiteSpace(explanationStatus))
                rows = rows.Where(r => r.ExplanationStatus == explanationStatus);

            var all = rows.ToList();
            result.Total = all.Count;
            result.Items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return result;
        }

        private async Task<List<AttendanceResponseDto>> SearchAttendanceWithLeavesAsync(DateOnly? fromDate, DateOnly? toDate, int? employeeId, string? status)
        {
            var finalFrom = fromDate ?? DateOnly.FromDateTime(AppTime.Now).AddDays(-30);
            var finalTo = toDate ?? DateOnly.FromDateTime(AppTime.Now);

            // Same window as the overtime, leave and shift lookups below; without dates this used to load the whole history
            // All statuses are loaded and the status filter is applied to the merged rows at the end: filtering here
            // hid the days people did work, so the absence step below marked every one of them "Absent"
            var records = await _attendanceRepository.SearchAttendanceAsync(finalFrom, finalTo, employeeId, null);
            
            // Dùng Dictionary để quản lý bản ghi theo (EmployeeId, Date)
            var attendanceMap = records
                .GroupBy(a => (a.EmployeeId, a.AttendanceDate))
                .ToDictionary(
                    g => g.Key, 
                    g => _mapper.Map<AttendanceResponseDto>(g.First())
                );

            // Fetch approved Overtime Requests
            var approvedOts = await _context.OvertimeRequests
                .AsNoTracking()
                .Where(or => or.Status == "Approved" &&
                             or.OvertimeDate >= finalFrom && 
                             or.OvertimeDate <= finalTo &&
                             (employeeId == null || or.EmployeeId == employeeId))
                .ToListAsync();

            // Fetch LeaveRequests
            // The employee filter is only added when given: "EmployeeId == (employeeId ?? EmployeeId)" kept SQL Server
            // from using an index on these tables
            var leaveQuery = _context.LeaveRequests
                .AsNoTracking()
                .Include(lr => lr.LeaveType)
                .Include(lr => lr.Employee)
                .Where(lr => lr.Status == "Approved" &&
                             lr.StartDate <= finalTo &&
                             lr.EndDate >= finalFrom);
            if (employeeId.HasValue)
                leaveQuery = leaveQuery.Where(lr => lr.EmployeeId == employeeId.Value);
            var leaves = await leaveQuery.ToListAsync();

            // Fetch Assignments
            var assignmentQuery = _context.ShiftAssignments
                .AsNoTracking()
                .Include(sa => sa.Shift)
                .Where(sa => sa.Status == "Active" &&
                             sa.AssignmentDate >= finalFrom &&
                             sa.AssignmentDate <= finalTo);
            if (employeeId.HasValue)
                assignmentQuery = assignmentQuery.Where(sa => sa.EmployeeId == employeeId.Value);
            var assignments = await assignmentQuery.ToListAsync();

            // A whole company over a quarter is tens of thousands of assignments; look them up by key, not by scanning
            var assignmentByDay = assignments
                .GroupBy(a => (a.EmployeeId, a.AssignmentDate))
                .ToDictionary(g => g.Key, g => g.First());

            // 1. Map Leave Requests
            foreach (var leave in leaves)
            {
                var start = leave.StartDate < finalFrom ? finalFrom : leave.StartDate;
                var end = leave.EndDate > finalTo ? finalTo : leave.EndDate;

                for (var date = start; date <= end; date = date.AddDays(1))
                {
                    var key = (EmployeeId: leave.EmployeeId, Date: date);
                    
                    if (!attendanceMap.ContainsKey(key) || 
                        attendanceMap[key].Status == "Absent" || 
                        attendanceMap[key].Status == "Vắng mặt")
                    {
                        assignmentByDay.TryGetValue((leave.EmployeeId, date), out var assignment);

                        var leaveRecord = new AttendanceResponseDto
                        {
                            AttendanceId = 0,
                            EmployeeId = leave.EmployeeId,
                            EmployeeName = leave.Employee?.FullName ?? (attendanceMap.ContainsKey(key) ? attendanceMap[key].EmployeeName : "Unknown"),
                            AttendanceDate = date,
                            ShiftId = assignment?.ShiftId,
                            ShiftName = assignment?.Shift?.ShiftName ?? "No Shift",
                            CheckInTime = null,
                            CheckOutTime = null,
                            WorkingHours = leave.LeaveType?.IsPaid == true ? (assignment?.Shift?.WorkingHours ?? 8) : 0,
                            OvertimeHours = 0,
                            LateMinutes = 0,
                            EarlyLeaveMinutes = 0,
                            Status = leave.LeaveType?.IsPaid == true ? "PaidLeave" : "UnpaidLeave",
                            Source = "LeaveRequest",
                            Remarks = (leave.LeaveType?.LeaveTypeName ?? "Nghỉ phép") + (assignment == null ? " (Mất phân ca)" : "") + ": " + leave.Reason
                        };
                        AttendanceHelper.ApplyShiftWindowMetadata(leaveRecord, assignment?.Shift, date);
                        attendanceMap[key] = leaveRecord;
                    }
                }
            }

            // 2. Generate virtual "Absent" records ("Holiday" on a day of the holiday calendar)
            if (status == null || status == "Absent" || status == "Holiday")
            {
                var today = DateOnly.FromDateTime(AppTime.Now);
                var holidays = await _context.Holidays.AsNoTracking()
                    .Where(h => h.HolidayDate >= finalFrom && h.HolidayDate <= finalTo)
                    .ToDictionaryAsync(h => h.HolidayDate);

                // Tránh N+1 bằng cách cache tên nhân viên
                var employeeNames = leaves.Select(l => new { l.EmployeeId, FullName = l.Employee?.FullName ?? "Unknown" })
                    .Concat(records.Select(r => new { r.EmployeeId, FullName = r.Employee?.FullName ?? "Unknown" }))
                    .DistinctBy(x => x.EmployeeId)
                    .ToDictionary(x => x.EmployeeId, x => x.FullName);

                // Employees with a shift but no attendance or leave in range (common when filtering by status):
                // fetch their names in one query instead of one per employee
                var missingNameIds = assignments.Select(a => a.EmployeeId).Distinct()
                    .Where(id => !employeeNames.ContainsKey(id)).ToList();
                if (missingNameIds.Count > 0)
                {
                    var missingNames = await _context.Employees.AsNoTracking()
                        .Where(e => missingNameIds.Contains(e.EmployeeId))
                        .Select(e => new { e.EmployeeId, e.FullName })
                        .ToListAsync();
                    foreach (var e in missingNames)
                        employeeNames[e.EmployeeId] = e.FullName;
                }

                foreach (var assignment in assignments)
                {
                    if (assignment.AssignmentDate >= today) continue;

                    var key = (EmployeeId: assignment.EmployeeId, Date: assignment.AssignmentDate);
                    if (!attendanceMap.ContainsKey(key))
                    {
                        if (!employeeNames.TryGetValue(assignment.EmployeeId, out var empName))
                        {
                             empName = await _context.Employees
                                .Where(e => e.EmployeeId == assignment.EmployeeId)
                                .Select(e => e.FullName)
                                .FirstOrDefaultAsync() ?? "Unknown";
                             employeeNames[assignment.EmployeeId] = empName;
                        }

                        holidays.TryGetValue(assignment.AssignmentDate, out var holiday);
                        var absentRecord = new AttendanceResponseDto
                        {
                            AttendanceId = 0,
                            EmployeeId = assignment.EmployeeId,
                            EmployeeName = empName,
                            AttendanceDate = assignment.AssignmentDate,
                            ShiftId = assignment.ShiftId,
                            ShiftName = assignment.Shift?.ShiftName ?? "No Shift",
                            CheckInTime = null,
                            CheckOutTime = null,
                            // A paid holiday is a full paid day; nobody is absent on a holiday
                            WorkingHours = holiday?.IsPaid == true ? (assignment.Shift?.WorkingHours ?? 8) : 0,
                            OvertimeHours = 0,
                            LateMinutes = 0,
                            EarlyLeaveMinutes = 0,
                            Status = holiday != null ? "Holiday" : "Absent",
                            Source = "System",
                            Remarks = holiday?.Name
                        };
                        attendanceMap[key] = absentRecord;
                    }
                }
            }

            var result = attendanceMap.Values.ToList();

            // Override IsLocked cho các bản ghi thuộc kỳ lương đã phê duyệt
            await ApplyPayrollLockOverrideAsync(result);

            // Leave rows are added whatever status was asked for, so filter the final rows
            var filtered = string.IsNullOrWhiteSpace(status) ? result : result.Where(r => r.Status == status);
            return filtered.OrderByDescending(r => r.AttendanceDate).ThenBy(r => r.EmployeeName).ToList();
        }

        private async System.Threading.Tasks.Task ApplyPayrollLockOverrideAsync(List<AttendanceResponseDto> records)
        {
            if (!records.Any()) return;

            var approvedPeriods = await _context.PayrollPeriods
                .Where(p => p.Status == "Approved" || p.Status == "Closed")
                .Select(p => new { p.PeriodId, p.StartDate, p.EndDate })
                .ToListAsync();

            if (!approvedPeriods.Any()) return;

            var employeeIds = records.Select(r => r.EmployeeId).Distinct().ToList();
            var periodIds = approvedPeriods.Select(p => p.PeriodId).ToList();

            var approvedPairs = await _context.PayrollRecords
                .Where(r => periodIds.Contains(r.PeriodId) && employeeIds.Contains(r.EmployeeId))
                .Select(r => new { r.PeriodId, r.EmployeeId })
                .ToListAsync();

            // Records × payroll rows grows fast for a whole company, so check membership in a set
            var approvedSet = approvedPairs.Select(e => (e.PeriodId, e.EmployeeId)).ToHashSet();

            foreach (var rec in records.Where(r => r.IsLocked != true))
            {
                var inApproved = approvedPeriods.Any(p =>
                    p.StartDate <= rec.AttendanceDate && p.EndDate >= rec.AttendanceDate &&
                    approvedSet.Contains((p.PeriodId, rec.EmployeeId)));
                if (inApproved) rec.IsLocked = true;
            }
        }

        private static DateTime MaxDateTime(DateTime a, DateTime b) => a > b ? a : b;
        private static DateTime MinDateTime(DateTime a, DateTime b) => a < b ? a : b;

        public async Task<AttendanceDetailResponseDto?> GetAttendanceDetailAsync(int employeeId, DateOnly date)
        {
            var attendance = await _attendanceRepository.GetAttendanceByEmployeeAndDateAsync(employeeId, date);
            if (attendance == null)
                return null;

            var logs = await _attendanceRepository.GetLogsByEmployeeAndDateAsync(employeeId, date);
            var dto = _mapper.Map<AttendanceResponseDto>(attendance);

            // Override IsLocked nếu thuộc kỳ lương đã phê duyệt
            if (dto.IsLocked != true)
            {
                var inApproved = await _context.PayrollPeriods
                    .AnyAsync(p => (p.Status == "Approved" || p.Status == "Closed") &&
                                   p.StartDate <= date && p.EndDate >= date &&
                                   _context.PayrollRecords.Any(r => r.PeriodId == p.PeriodId && r.EmployeeId == employeeId));
                if (inApproved) dto.IsLocked = true;
            }

            return new AttendanceDetailResponseDto
            {
                Attendance = dto,
                Logs = logs.Select(l => _mapper.Map<AttendanceLogResponseDto>(l)).ToList()
            };
        }

        public async Task<AttendanceResponseDto> ManualAdjustAttendanceAsync(int attendanceId, int approverId, ManualAdjustAttendanceDto dto)
        {
            var attendance = await _attendanceRepository.GetAttendanceByIdAsync(attendanceId)
                ?? throw new InvalidOperationException("Không tìm thấy bản ghi chấm công.");

            if (attendance.IsLocked ?? false)
                throw new InvalidOperationException("Bản ghi chấm công đã bị khóa.");

            attendance.CheckInTime = dto.CheckInTime;
            attendance.CheckOutTime = dto.CheckOutTime;
            attendance.Status = dto.Status;
            attendance.IsManualAdjusted = true;
            attendance.Source = dto.Source ?? "Manual";
            attendance.Remarks = dto.Remarks;
            attendance.ApprovedBy = approverId;
            attendance.ApprovedDate = AppTime.Now;
            attendance.ModifiedDate = AppTime.Now;
            attendance.ModifiedBy = approverId;

            if (attendance.CheckInTime.HasValue && attendance.CheckOutTime.HasValue)
            {
                var hours = (decimal)(attendance.CheckOutTime.Value - attendance.CheckInTime.Value).TotalHours;
                attendance.WorkingHours = Math.Round(hours, 2);
            }
            else
            {
                attendance.WorkingHours = null;
            }

            await _attendanceRepository.UpdateAttendanceAsync(attendance);
            await _attendanceRepository.SaveChangesAsync();

            var refreshed = await _attendanceRepository.GetAttendanceByIdAsync(attendance.AttendanceId) ?? attendance;
            return _mapper.Map<AttendanceResponseDto>(refreshed);
        }

        public async Task<AttendanceResponseDto> ManualCreateAttendanceAsync(int approverId, ManualCreateAttendanceDto dto)
        {
            var existing = await _attendanceRepository.GetAttendanceByEmployeeAndDateAsync(dto.EmployeeId, dto.AttendanceDate);
            if (existing != null)
                throw new InvalidOperationException("Nhân viên đã có bản ghi chấm công trong ngày này.");

            decimal? workingHours = null;
            if (dto.CheckInTime.HasValue && dto.CheckOutTime.HasValue)
            {
                workingHours = Math.Round((decimal)(dto.CheckOutTime.Value - dto.CheckInTime.Value).TotalHours, 2);
            }

            var attendance = new AttendanceRecord
            {
                EmployeeId = dto.EmployeeId,
                AttendanceDate = dto.AttendanceDate,
                ShiftId = dto.ShiftId,
                CheckInTime = dto.CheckInTime,
                CheckOutTime = dto.CheckOutTime,
                WorkingHours = workingHours,
                OvertimeHours = 0, // This will be dynamically synced in history view
                LateMinutes = 0,
                EarlyLeaveMinutes = 0,
                Status = dto.Status,
                Source = dto.Source ?? "Manual",
                IsManualAdjusted = true,
                IsLocked = false,
                ApprovedBy = approverId,
                ApprovedDate = AppTime.Now,
                Remarks = dto.Remarks,
                CreatedDate = AppTime.Now,
                ModifiedDate = null,
                ModifiedBy = null
            };

            await _attendanceRepository.AddAttendanceAsync(attendance);
            await _attendanceRepository.SaveChangesAsync();

            var created = await _attendanceRepository.GetAttendanceByEmployeeAndDateAsync(dto.EmployeeId, dto.AttendanceDate)
                ?? throw new InvalidOperationException("Không tìm thấy bản ghi sau khi tạo.");

            return _mapper.Map<AttendanceResponseDto>(created);
        }

        public async System.Threading.Tasks.Task LockAttendanceAsync(int attendanceId, int userId)
        {
            var attendance = await _attendanceRepository.GetAttendanceByIdAsync(attendanceId)
                ?? throw new InvalidOperationException("Không tìm thấy bản ghi chấm công.");

            attendance.IsLocked = true;
            attendance.ModifiedDate = AppTime.Now;
            attendance.ModifiedBy = userId;

            await _attendanceRepository.UpdateAttendanceAsync(attendance);
            await _attendanceRepository.SaveChangesAsync();
        }

        public async System.Threading.Tasks.Task UnlockAttendanceAsync(int attendanceId, int userId)
        {
            var attendance = await _attendanceRepository.GetAttendanceByIdAsync(attendanceId)
                ?? throw new InvalidOperationException("Không tìm thấy bản ghi chấm công.");

            var isPayrollLocked = await _context.PayrollPeriods
                .AnyAsync(p => (p.Status == "Approved" || p.Status == "Closed") &&
                               p.StartDate <= attendance.AttendanceDate &&
                               p.EndDate >= attendance.AttendanceDate &&
                               _context.PayrollRecords.Any(r => r.PeriodId == p.PeriodId && r.EmployeeId == attendance.EmployeeId));

            if (isPayrollLocked)
                throw new InvalidOperationException("Bản ghi này thuộc kỳ lương đã được phê duyệt, không thể mở khóa.");

            attendance.IsLocked = false;
            attendance.ModifiedDate = AppTime.Now;
            attendance.ModifiedBy = userId;

            await _attendanceRepository.UpdateAttendanceAsync(attendance);
            await _attendanceRepository.SaveChangesAsync();
        }

        public async Task<List<AttendanceLogResponseDto>> GetLogsAsync(int employeeId, DateOnly date)
        {
            var logs = await _attendanceRepository.GetLogsByEmployeeAndDateAsync(employeeId, date);
            return logs.Select(l => _mapper.Map<AttendanceLogResponseDto>(l)).ToList();
        }

        

        public async Task<AttendanceResponseDto> SubmitExplanationAsync(int employeeId, int attendanceId, SubmitExplanationDto dto)
        {
            var attendance = await _attendanceRepository.GetAttendanceByIdAsync(attendanceId)
                ?? throw new InvalidOperationException("Không tìm thấy bản ghi chấm công.");

            if (attendance.EmployeeId != employeeId)
                throw new InvalidOperationException("Bạn không có quyền giải trình cho bản ghi này.");
                
            if (attendance.IsLocked == true)
                throw new InvalidOperationException("Bản ghi chấm công đã bị khóa.");

            await AttendanceHelper.ApplyExplanationSubmissionAsync(
                _context,
                _attendanceRepository,
                attendance,
                employeeId,
                dto.Message,
                dto.ExplanationType,
                dto.LeaveTypeId,
                dto.RequestedCheckInTime,
                dto.RequestedCheckOutTime);

            await _attendanceRepository.UpdateAttendanceAsync(attendance);
            await _attendanceRepository.SaveChangesAsync();
            await NotifyExplanationSubmittedAsync(attendance);

            var refreshed = await _attendanceRepository.GetAttendanceByIdAsync(attendance.AttendanceId) ?? attendance;
            return _mapper.Map<AttendanceResponseDto>(refreshed);
        }

        public async Task<AttendanceResponseDto> SubmitAbsentExplanationAsync(int employeeId, DateOnly date, SubmitAbsentExplanationDto dto)
        {
            // Verify if there was a shift assignment
            var assignment = await _context.ShiftAssignments
                .Include(sa => sa.Shift)
                .FirstOrDefaultAsync(sa => sa.EmployeeId == employeeId && sa.AssignmentDate == date && sa.Status == "Active");

            if (assignment == null)
            {
                throw new InvalidOperationException("Bạn không có ca làm việc vào ngày này để giải trình.");
            }

            // Check if there is already an attendance record
            var attendance = await _attendanceRepository.GetAttendanceByEmployeeAndDateAsync(employeeId, date);
            if (attendance != null)
            {
                if (attendance.IsLocked == true)
                    throw new InvalidOperationException("Bản ghi chấm công đã bị khóa.");

            }
            else
            {
                attendance = new AttendanceRecord
                {
                    EmployeeId = employeeId,
                    AttendanceDate = date,
                    ShiftId = assignment.ShiftId,
                    CheckInTime = null,
                    CheckOutTime = null,
                    WorkingHours = 0,
                    OvertimeHours = 0,
                    LateMinutes = 0,
                    EarlyLeaveMinutes = 0,
                    Status = "Absent",
                    Source = "System",
                    IsManualAdjusted = false,
                    IsLocked = false,
                    CreatedDate = AppTime.Now
                };
                await _attendanceRepository.AddAttendanceAsync(attendance);
            }

            await AttendanceHelper.ApplyExplanationSubmissionAsync(
                _context,
                _attendanceRepository,
                attendance,
                employeeId,
                dto.Message,
                dto.ExplanationType,
                dto.LeaveTypeId,
                dto.RequestedCheckInTime,
                dto.RequestedCheckOutTime);
            await _attendanceRepository.SaveChangesAsync();
            await NotifyExplanationSubmittedAsync(attendance);
            var refreshed = await _attendanceRepository.GetAttendanceByEmployeeAndDateAsync(employeeId, date) ?? attendance;
            return _mapper.Map<AttendanceResponseDto>(refreshed);
        }

        public async Task<AttendanceResponseDto> ApproveExplanationAsync(int managerId, int attendanceId, ApproveExplanationDto dto)
        {
            var attendance = await _attendanceRepository.GetAttendanceByIdAsync(attendanceId)
                ?? throw new InvalidOperationException("Không tìm thấy bản ghi chấm công.");

            if (!string.Equals(attendance.ExplanationStatus, "Pending", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Phiếu giải trình này không còn ở trạng thái chờ duyệt.");

            var today = DateOnly.FromDateTime(AppTime.Now);
            if (attendance.AttendanceDate >= today)
                throw new InvalidOperationException("Phiếu giải trình cho ngày hiện tại chỉ có thể được phê duyệt sau khi ngày làm việc kết thúc (từ ngày mai).");

            if (dto.IsApproved)
            {
                if (string.Equals(attendance.ExplanationType, "LeaveRequest", StringComparison.OrdinalIgnoreCase))
                {
                    await AttendanceHelper.CreateApprovedLeaveFromExplanationAsync(_context, managerId, attendance, dto.Response);
                }
                else
                {
                    await AttendanceHelper.ApplyApprovedRegularizationAsync(_attendanceRepository, attendance, dto.ManualCheckInTime, dto.ManualCheckOutTime);
                }

                attendance.ExplanationStatus = "Approved";
            }
            else
            {
                attendance.ExplanationStatus = "Rejected";
            }

            attendance.ExplanationResponse = dto.Response;
            attendance.ApprovedBy = managerId;
            attendance.ApprovedDate = AppTime.Now;
            attendance.ModifiedDate = AppTime.Now;
            attendance.ModifiedBy = managerId;

            await _attendanceRepository.UpdateAttendanceAsync(attendance);
            if (await _notifications.GetUserIdOfEmployeeAsync(attendance.EmployeeId) is { } requesterId)
            {
                var approved = attendance.ExplanationStatus == "Approved";
                _notifications.Notify(requesterId, NotificationTypes.ExplanationResult,
                    approved ? "Giải trình chấm công đã được duyệt" : "Giải trình chấm công bị từ chối",
                    $"Giải trình chấm công ngày {attendance.AttendanceDate:dd/MM/yyyy} đã {(approved ? "được duyệt" : "bị từ chối")}.",
                    "AttendanceRecord", attendance.AttendanceId);
            }
            await _attendanceRepository.SaveChangesAsync();

            var refreshed = await _attendanceRepository.GetAttendanceByIdAsync(attendance.AttendanceId) ?? attendance;
            return _mapper.Map<AttendanceResponseDto>(refreshed);
        }

    }
}


