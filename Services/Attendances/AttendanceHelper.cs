using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HRManagement.Models;
using HRManagement.DTOs.Attendances;
using HRManagement.DataAcess.Interfaces;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Services.Attendances
{
    public static class AttendanceHelper
    {
        public class ShiftWindow
        {
            public DateTime ShiftStart { get; init; }
            public DateTime ShiftEnd { get; init; }
            public DateTime AllowedCheckInFrom { get; init; }
            public DateTime AllowedCheckInTo { get; init; }
            public DateTime AllowedCheckOutFrom { get; init; }
            public DateTime AllowedCheckOutTo { get; init; }
        }

        public static ShiftWindow BuildShiftWindow(DateOnly attendanceDate, Shift shift)
        {
            var shiftStart = attendanceDate.ToDateTime(shift.StartTime);
            var shiftEnd = attendanceDate.ToDateTime(shift.EndTime);
            if (shift.IsOvernight == true && shiftEnd <= shiftStart)
                shiftEnd = shiftEnd.AddDays(1);

            return new ShiftWindow
            {
                ShiftStart = shiftStart,
                ShiftEnd = shiftEnd,
                AllowedCheckInFrom = shiftStart.AddMinutes(-(shift.EarlyCheckInMinutes ?? 30)),
                AllowedCheckInTo = shiftStart.AddMinutes(shift.LatestCheckInMinutes ?? 120),
                AllowedCheckOutFrom = shiftEnd.AddMinutes(-(shift.EarliestCheckOutMinutes ?? 120)),
                AllowedCheckOutTo = shiftEnd.AddMinutes(shift.LatestCheckOutMinutes ?? 240)
            };
        }

        public static void ApplyShiftWindowMetadata(AttendanceResponseDto dto, Shift? shift, DateOnly attendanceDate)
        {
            if (shift == null)
                return;

            var window = BuildShiftWindow(attendanceDate, shift);
            dto.ShiftStartTime = shift.StartTime.ToTimeSpan();
            dto.ShiftEndTime = shift.EndTime.ToTimeSpan();
            dto.ShiftIsOvernight = shift.IsOvernight;
            dto.AllowedCheckInFrom = window.AllowedCheckInFrom;
            dto.AllowedCheckInTo = window.AllowedCheckInTo;
            dto.AllowedCheckOutFrom = window.AllowedCheckOutFrom;
            dto.AllowedCheckOutTo = window.AllowedCheckOutTo;
        }

        public static async Task ApplyExplanationSubmissionAsync(
            HrmsDbContext context,
            IAttendanceRepository attendanceRepository,
            AttendanceRecord attendance,
            int employeeId,
            string message,
            string? explanationType,
            int? leaveTypeId,
            TimeSpan? requestedCheckInTime,
            TimeSpan? requestedCheckOutTime)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new InvalidOperationException("Vui lòng nhập lý do giải trình.");

            var normalizedType = NormalizeExplanationType(explanationType, leaveTypeId, requestedCheckInTime, requestedCheckOutTime);
            var shift = await ResolveShiftForAttendanceAsync(attendanceRepository, attendance);

            attendance.ExplanationMessage = message.Trim();
            attendance.ExplanationStatus = "Pending";
            attendance.ExplanationResponse = null;
            attendance.ExplanationType = normalizedType;
            attendance.ExplanationLeaveTypeId = null;
            attendance.ExplanationRequestedCheckInTime = null;
            attendance.ExplanationRequestedCheckOutTime = null;
            attendance.ModifiedDate = AppTime.Now;
            attendance.ModifiedBy = employeeId;

            if (string.Equals(normalizedType, "LeaveRequest", StringComparison.OrdinalIgnoreCase))
            {
                var leaveType = await ValidateLeaveExplanationAsync(context, attendance, leaveTypeId);
                attendance.ExplanationLeaveTypeId = leaveType.LeaveTypeId;
            }
            else if (string.Equals(normalizedType, "Regularization", StringComparison.OrdinalIgnoreCase))
            {
                ValidateRegularizationRequest(attendance, shift, requestedCheckInTime, requestedCheckOutTime);
                attendance.ExplanationRequestedCheckInTime = requestedCheckInTime;
                attendance.ExplanationRequestedCheckOutTime = requestedCheckOutTime;
            }
        }

        public static string? NormalizeExplanationType(
            string? explanationType,
            int? leaveTypeId,
            TimeSpan? requestedCheckInTime,
            TimeSpan? requestedCheckOutTime)
        {
            if (leaveTypeId.HasValue)
                return "LeaveRequest";

            if (requestedCheckInTime.HasValue || requestedCheckOutTime.HasValue)
                return "Regularization";

            if (string.IsNullOrWhiteSpace(explanationType))
                return null;

            if (string.Equals(explanationType, "Leave", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(explanationType, "LeaveRequest", StringComparison.OrdinalIgnoreCase))
                return "LeaveRequest";

            if (string.Equals(explanationType, "Regularization", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(explanationType, "Correction", StringComparison.OrdinalIgnoreCase))
                return "Regularization";

            throw new InvalidOperationException("Loại giải trình không hợp lệ.");
        }

        public static async Task<LeaveType> ValidateLeaveExplanationAsync(HrmsDbContext context, AttendanceRecord attendance, int? leaveTypeId)
        {
            if (!leaveTypeId.HasValue)
                throw new InvalidOperationException("Vui lòng chọn loại phép cho ngày vắng mặt.");

            if (attendance.CheckInTime.HasValue || attendance.CheckOutTime.HasValue)
                throw new InvalidOperationException("Không thể chuyển bản ghi đã có giờ chấm công thành đơn nghỉ phép.");

            if (!string.Equals(attendance.Status, "Absent", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Đơn nghỉ phép chỉ áp dụng cho ngày vắng mặt.");

            var leaveType = await context.LeaveTypes
                .FirstOrDefaultAsync(x => x.LeaveTypeId == leaveTypeId.Value && x.IsActive)
                ?? throw new InvalidOperationException("Loại phép không hợp lệ hoặc đã bị vô hiệu hóa.");

            var hasOverlap = await context.LeaveRequests.AnyAsync(x =>
                x.EmployeeId == attendance.EmployeeId &&
                (x.Status == "Pending" || x.Status == "Approved") &&
                attendance.AttendanceDate >= x.StartDate &&
                attendance.AttendanceDate <= x.EndDate);

            if (hasOverlap)
                throw new InvalidOperationException("Ngày này đã có đơn nghỉ phép chờ duyệt hoặc đã được duyệt.");

            if (leaveType.AnnualEntitlement > 0)
            {
                var leaveBalance = await context.LeaveBalances.FirstOrDefaultAsync(x =>
                    x.EmployeeId == attendance.EmployeeId &&
                    x.LeaveTypeId == leaveType.LeaveTypeId &&
                    x.Year == attendance.AttendanceDate.Year);

                var currentBalance = leaveBalance?.RemainingDays
                    ?? (leaveBalance != null
                        ? leaveBalance.TotalEntitlement - leaveBalance.UsedDays + leaveBalance.CarriedForward
                        : 0);

                if (currentBalance < 1)
                    throw new InvalidOperationException("Số dư phép không đủ để tạo đơn nghỉ cho ngày này.");
            }

            return leaveType;
        }

        public static void ValidateRegularizationRequest(
            AttendanceRecord attendance,
            Shift? shift,
            TimeSpan? requestedCheckInTime,
            TimeSpan? requestedCheckOutTime)
        {
            var needsCheckIn = !attendance.CheckInTime.HasValue;
            var needsCheckOut = !attendance.CheckOutTime.HasValue;

            if (requestedCheckInTime.HasValue && !needsCheckIn)
                throw new InvalidOperationException("Bản ghi này đã có giờ check-in, không cần bổ sung lại.");

            if (requestedCheckOutTime.HasValue && !needsCheckOut)
                throw new InvalidOperationException("Bản ghi này đã có giờ check-out, không cần bổ sung lại.");

            if (needsCheckIn && !requestedCheckInTime.HasValue)
                throw new InvalidOperationException("Vui lòng chọn giờ check-in theo ca làm việc của ngày đó.");

            if (needsCheckOut && !requestedCheckOutTime.HasValue)
                throw new InvalidOperationException("Vui lòng chọn giờ check-out theo ca làm việc của ngày đó.");

            if (!requestedCheckInTime.HasValue && !requestedCheckOutTime.HasValue)
                return;

            if (shift == null)
                throw new InvalidOperationException("Không tìm thấy ca làm việc để kiểm tra khung giờ bổ sung.");

            var window = BuildShiftWindow(attendance.AttendanceDate, shift);
            var finalCheckIn = attendance.CheckInTime;
            var finalCheckOut = attendance.CheckOutTime;

            if (requestedCheckInTime.HasValue)
            {
                finalCheckIn = ComposeRequestedDateTime(attendance.AttendanceDate, requestedCheckInTime.Value, shift, false);
                ValidateRequestedTime(finalCheckIn.Value, window.AllowedCheckInFrom, window.AllowedCheckInTo, "Giờ check-in bổ sung nằm ngoài khung thời gian được phép của ca làm việc.");
            }

            if (requestedCheckOutTime.HasValue)
            {
                finalCheckOut = ComposeRequestedDateTime(attendance.AttendanceDate, requestedCheckOutTime.Value, shift, true);
                ValidateRequestedTime(finalCheckOut.Value, window.AllowedCheckOutFrom, window.AllowedCheckOutTo, "Giờ check-out bổ sung nằm ngoài khung thời gian được phép của ca làm việc.");
            }

            if (finalCheckIn.HasValue && finalCheckOut.HasValue && finalCheckOut.Value <= finalCheckIn.Value)
                throw new InvalidOperationException("Giờ check-out phải lớn hơn giờ check-in.");
        }

        public static async Task ApplyApprovedRegularizationAsync(
            IAttendanceRepository attendanceRepository,
            AttendanceRecord attendance,
            TimeSpan? overrideCheckInTime,
            TimeSpan? overrideCheckOutTime)
        {
            var shift = await ResolveShiftForAttendanceAsync(attendanceRepository, attendance);
            var shouldApplyTimes =
                overrideCheckInTime.HasValue ||
                overrideCheckOutTime.HasValue ||
                attendance.ExplanationRequestedCheckInTime.HasValue ||
                attendance.ExplanationRequestedCheckOutTime.HasValue ||
                !attendance.CheckInTime.HasValue ||
                !attendance.CheckOutTime.HasValue;

            if (!shouldApplyTimes)
                return;

            var requestedCheckInTime = overrideCheckInTime ?? attendance.ExplanationRequestedCheckInTime;
            var requestedCheckOutTime = overrideCheckOutTime ?? attendance.ExplanationRequestedCheckOutTime;

            ValidateRegularizationRequest(attendance, shift, requestedCheckInTime, requestedCheckOutTime);

            if (requestedCheckInTime.HasValue)
            {
                attendance.CheckInTime = ComposeRequestedDateTime(attendance.AttendanceDate, requestedCheckInTime.Value, shift!, false);
                attendance.IsManualAdjusted = true;
            }

            if (requestedCheckOutTime.HasValue)
            {
                attendance.CheckOutTime = ComposeRequestedDateTime(attendance.AttendanceDate, requestedCheckOutTime.Value, shift!, true);
                attendance.IsManualAdjusted = true;
            }

            RecalculateAttendanceSummary(attendance, shift);
        }

        public static async Task CreateApprovedLeaveFromExplanationAsync(HrmsDbContext context, int managerId, AttendanceRecord attendance, string? managerComment)
        {
            var leaveType = await ValidateLeaveExplanationAsync(context, attendance, attendance.ExplanationLeaveTypeId);
            LeaveBalance? leaveBalance = null;

            if (leaveType.AnnualEntitlement > 0)
            {
                leaveBalance = await context.LeaveBalances.FirstOrDefaultAsync(x =>
                    x.EmployeeId == attendance.EmployeeId &&
                    x.LeaveTypeId == leaveType.LeaveTypeId &&
                    x.Year == attendance.AttendanceDate.Year);

                if (leaveBalance == null)
                    throw new InvalidOperationException("Không tìm thấy số dư phép cho loại nghỉ đã chọn.");

                var currentBalance = leaveBalance.RemainingDays
                    ?? leaveBalance.TotalEntitlement - leaveBalance.UsedDays + leaveBalance.CarriedForward;

                if (currentBalance < 1)
                    throw new InvalidOperationException("Số dư phép không đủ để duyệt ngày nghỉ này.");

                leaveBalance.UsedDays += 1;
                leaveBalance.RemainingDays = (leaveBalance.TotalEntitlement + leaveBalance.CarriedForward) - leaveBalance.UsedDays;
                leaveBalance.LastUpdated = AppTime.Now;
            }

            var leaveRequest = new LeaveRequest
            {
                RequestNumber = await GenerateLeaveRequestNumberAsync(context),
                EmployeeId = attendance.EmployeeId,
                LeaveTypeId = leaveType.LeaveTypeId,
                StartDate = attendance.AttendanceDate,
                EndDate = attendance.AttendanceDate,
                NumberOfDays = 1,
                Reason = attendance.ExplanationMessage,
                Status = "Approved",
                SubmittedDate = AppTime.Now,
                ReviewedDate = AppTime.Now,
                ReviewedBy = managerId,
                ReviewerComments = managerComment,
                ApprovedDate = AppTime.Now,
                ApprovedBy = managerId,
                TargetApproverId = managerId
            };

            context.LeaveRequests.Add(leaveRequest);

            attendance.WorkingHours = 0;
            attendance.OvertimeHours = 0;
            attendance.LateMinutes = 0;
            attendance.EarlyLeaveMinutes = 0;
            attendance.Status = "Absent";
        }

        public static async Task<string> GenerateLeaveRequestNumberAsync(HrmsDbContext context)
        {
            var today = AppTime.Now;
            var prefix = $"LR-EXP-{today:yyyyMMdd}";
            var countToday = await context.LeaveRequests.CountAsync(x => x.SubmittedDate.Date == today.Date);
            return $"{prefix}-{countToday + 1:D3}";
        }

        public static async Task<Shift?> ResolveShiftForAttendanceAsync(IAttendanceRepository attendanceRepository, AttendanceRecord attendance)
        {
            if (attendance.Shift != null)
                return attendance.Shift;

            if (attendance.ShiftId.HasValue)
                return await attendanceRepository.GetShiftByIdAsync(attendance.ShiftId.Value);

            var assignment = await attendanceRepository.GetShiftAssignmentByEmployeeAndDateAsync(attendance.EmployeeId, attendance.AttendanceDate);
            return assignment?.Shift;
        }

        public static void RecalculateAttendanceSummary(AttendanceRecord attendance, Shift? shift)
        {
            if (attendance.CheckInTime.HasValue && attendance.CheckOutTime.HasValue)
            {
                var hours = (decimal)(attendance.CheckOutTime.Value - attendance.CheckInTime.Value).TotalHours;
                attendance.WorkingHours = Math.Round(hours, 2);
                attendance.OvertimeHours = 0;
                attendance.LateMinutes = 0;
                attendance.EarlyLeaveMinutes = 0;

                if (shift != null)
                {
                    var window = BuildShiftWindow(attendance.AttendanceDate, shift);

                    if (attendance.CheckInTime.Value > window.ShiftStart)
                        attendance.LateMinutes = (int)Math.Floor((attendance.CheckInTime.Value - window.ShiftStart).TotalMinutes);

                    if (attendance.CheckOutTime.Value < window.ShiftEnd)
                    {
                        attendance.EarlyLeaveMinutes = (int)Math.Floor((window.ShiftEnd - attendance.CheckOutTime.Value).TotalMinutes);
                    }
                    else if (attendance.CheckOutTime.Value > window.ShiftEnd)
                    {
                        var overtime = (decimal)(attendance.CheckOutTime.Value - window.ShiftEnd).TotalHours;
                        attendance.OvertimeHours = overtime > 0 ? Math.Round(Math.Min(overtime, 9999m), 2) : 0;
                    }
                }

                if ((attendance.LateMinutes ?? 0) > 0)
                    attendance.Status = "Late";
                else if ((attendance.EarlyLeaveMinutes ?? 0) > 0)
                    attendance.Status = "Incomplete";
                else
                    attendance.Status = "Present";
            }
            else
            {
                attendance.WorkingHours = null;
                attendance.OvertimeHours = 0;
                attendance.LateMinutes = 0;
                attendance.EarlyLeaveMinutes = 0;
                attendance.Status = !attendance.CheckInTime.HasValue && !attendance.CheckOutTime.HasValue ? "Absent" : "Incomplete";
            }
        }

        public static DateTime ComposeRequestedDateTime(DateOnly attendanceDate, TimeSpan value, Shift shift, bool isCheckOut)
        {
            var dateTime = attendanceDate.ToDateTime(TimeOnly.FromTimeSpan(value));
            if (isCheckOut && shift.IsOvernight == true && value < shift.StartTime.ToTimeSpan())
                dateTime = dateTime.AddDays(1);
            return dateTime;
        }

        public static void ValidateRequestedTime(DateTime value, DateTime from, DateTime to, string message)
        {
            if (value < from || value > to)
                throw new InvalidOperationException(message);
        }

        public static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371e3; // metres
            var p1 = lat1 * Math.PI / 180;
            var p2 = lat2 * Math.PI / 180;
            var dp = (lat2 - lat1) * Math.PI / 180;
            var dl = (lon2 - lon1) * Math.PI / 180;

            var a = Math.Sin(dp / 2) * Math.Sin(dp / 2) +
                    Math.Cos(p1) * Math.Cos(p2) *
                    Math.Sin(dl / 2) * Math.Sin(dl / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c; 
        }

        public static async Task<bool> IsValidLocation(HrmsDbContext context, double? lat, double? lon)
        {
            if (!lat.HasValue || !lon.HasValue) return false;

            var settings = await context.SystemSettings
                .Where(s => s.SettingKey == "OfficeLatitude" || s.SettingKey == "OfficeLongitude" || s.SettingKey == "AttendanceAllowedRadius")
                .ToListAsync();

            var latSetting = settings.FirstOrDefault(s => s.SettingKey == "OfficeLatitude");
            var lngSetting = settings.FirstOrDefault(s => s.SettingKey == "OfficeLongitude");
            var radSetting = settings.FirstOrDefault(s => s.SettingKey == "AttendanceAllowedRadius");

            if (latSetting == null || lngSetting == null) return true;

            if (double.TryParse(latSetting.SettingValue, System.Globalization.CultureInfo.InvariantCulture, out double cLat) &&
                double.TryParse(lngSetting.SettingValue, System.Globalization.CultureInfo.InvariantCulture, out double cLng))
            {
                double radius = 100;
                if (radSetting != null && double.TryParse(radSetting.SettingValue, System.Globalization.CultureInfo.InvariantCulture, out double r))
                    radius = r;

                return CalculateDistance(cLat, cLng, lat.Value, lon.Value) <= radius;
            }

            return true;
        }

        public static async Task<bool> IsValidIp(HrmsDbContext context, string? ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return false;

            var setting = await context.SystemSettings
                .FirstOrDefaultAsync(s => s.SettingKey == "AllowedIpAddresses");

            if (setting == null || string.IsNullOrWhiteSpace(setting.SettingValue)) return true;

            var allowedIps = setting.SettingValue
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return allowedIps.Any(ip => ip == ipAddress.Trim());
        }

        public static async Task<bool> IsValidCheckIn(HrmsDbContext context, CheckInRequestDto dto)
            => await IsValidCheckIn(context, dto.Latitude, dto.Longitude, dto.IpAddress);

        public static async Task<bool> IsValidCheckIn(HrmsDbContext context, CheckOutRequestDto dto)
            => await IsValidCheckIn(context, dto.Latitude, dto.Longitude, dto.IpAddress);

        public static async Task<bool> IsValidCheckIn(HrmsDbContext context, double? latitude, double? longitude, string? ipAddress)
        {
            var methodSetting = await context.SystemSettings
                .FirstOrDefaultAsync(s => s.SettingKey == "CheckInMethod");
            var method = methodSetting?.SettingValue ?? "Location";

            return method switch
            {
                "IP"     => await IsValidIp(context, ipAddress),
                "Either" => await IsValidLocation(context, latitude, longitude) || await IsValidIp(context, ipAddress),
                _        => await IsValidLocation(context, latitude, longitude),
            };
        }
        public static (DateTime Earliest, DateTime Latest) CalculateCheckInWindow(Shift shift, DateOnly date, List<OvertimeRequest> approvedOts)
        {
            var shiftStart = date.ToDateTime(shift.StartTime);
            var earlyCheckInMinutes = shift.EarlyCheckInMinutes ?? 30;
            var latestCheckInMinutes = shift.LatestCheckInMinutes ?? 120;

            var earliestCheckIn = shiftStart.AddMinutes(-earlyCheckInMinutes);
            var latestCheckIn = shiftStart.AddMinutes(latestCheckInMinutes);

            if (approvedOts.Any())
            {
                var minOtStart = approvedOts.Min(o => date.ToDateTime(o.StartTime));
                if (minOtStart < shiftStart)
                {
                    earliestCheckIn = minOtStart.AddMinutes(-earlyCheckInMinutes);
                }
            }

            return (earliestCheckIn, latestCheckIn);
        }

        public static (DateTime Earliest, DateTime Latest) CalculateCheckOutWindow(Shift shift, DateOnly date, List<OvertimeRequest> approvedOts)
        {
            var shiftEnd = date.ToDateTime(shift.EndTime);
            if (shift.IsOvernight == true) shiftEnd = shiftEnd.AddDays(1);

            var earliestCheckOutMinutes = shift.EarliestCheckOutMinutes ?? 120;
            var latestCheckOutMinutes = shift.LatestCheckOutMinutes ?? 240;

            var earliestCheckOut = shiftEnd.AddMinutes(-earliestCheckOutMinutes);
            var latestCheckOut = shiftEnd.AddMinutes(latestCheckOutMinutes);

            if (approvedOts.Any())
            {
                var maxOtEnd = approvedOts.Max(o => {
                    var end = date.ToDateTime(o.EndTime);
                    if (o.EndTime <= o.StartTime) end = end.AddDays(1);
                    return end;
                });

                if (maxOtEnd > shiftEnd)
                {
                    latestCheckOut = maxOtEnd.AddMinutes(latestCheckOutMinutes);
                }
            }

            return (earliestCheckOut, latestCheckOut);
        }

        public record OvertimeMetrics(decimal ActualHours, decimal ApprovedHours, decimal PayrollHours);

        public static OvertimeMetrics CalculateOvertimeMetrics(DateTime? checkIn, DateTime? checkOut, DateOnly attendanceDate, Shift? shift, List<OvertimeRequest> approvedOts)
        {
            decimal approvedTotal = approvedOts.Sum(o => o.TotalHours);
            decimal actualOtHours = 0;
            decimal payrollOtHours = 0;

            if (checkIn.HasValue && checkOut.HasValue)
            {
                var checkInTime = checkIn.Value;
                var checkOutTime = checkOut.Value;
                var baseDate = attendanceDate.ToDateTime(TimeOnly.MinValue);

                if (shift != null)
                {
                    var shiftStart = baseDate.Add(shift.StartTime.ToTimeSpan());
                    var shiftEnd = baseDate.Add(shift.EndTime.ToTimeSpan());
                    if (shift.IsOvernight == true) shiftEnd = shiftEnd.AddDays(1);

                    if (checkInTime < shiftStart)
                    {
                        var earlyEnd = checkOutTime < shiftStart ? checkOutTime : shiftStart;
                        actualOtHours += (decimal)(earlyEnd - checkInTime).TotalHours;
                    }
                    if (checkOutTime > shiftEnd)
                    {
                        var lateStart = checkInTime > shiftEnd ? checkInTime : shiftEnd;
                        actualOtHours += (decimal)(checkOutTime - lateStart).TotalHours;
                    }

                    foreach (var ot in approvedOts)
                    {
                        var otStart = baseDate.Add(ot.StartTime.ToTimeSpan());
                        var otEnd = baseDate.Add(ot.EndTime.ToTimeSpan());
                        if (otEnd <= otStart) otEnd = otEnd.AddDays(1);

                        var bStart = MaxDateTime(checkInTime, otStart);
                        var bEnd = MinDateTime(shiftStart, MinDateTime(checkOutTime, otEnd));
                        if (bEnd > bStart) payrollOtHours += (decimal)(bEnd - bStart).TotalHours;

                        var aStart = MaxDateTime(shiftEnd, MaxDateTime(checkInTime, otStart));
                        var aEnd = MinDateTime(checkOutTime, otEnd);
                        if (aEnd > aStart) payrollOtHours += (decimal)(aEnd - aStart).TotalHours;
                    }
                }
                else
                {
                    actualOtHours = (decimal)(checkOutTime - checkInTime).TotalHours;
                    foreach (var ot in approvedOts)
                    {
                        var otStart = baseDate.Add(ot.StartTime.ToTimeSpan());
                        var otEnd = baseDate.Add(ot.EndTime.ToTimeSpan());
                        if (otEnd <= otStart) otEnd = otEnd.AddDays(1);

                        var overlapStart = MaxDateTime(checkInTime, otStart);
                        var overlapEnd = MinDateTime(checkOutTime, otEnd);
                        if (overlapEnd > overlapStart) payrollOtHours += (decimal)(overlapEnd - overlapStart).TotalHours;
                    }
                }
            }

            return new OvertimeMetrics(
                Math.Round(actualOtHours, 2),
                Math.Round(approvedTotal, 2),
                Math.Round(payrollOtHours, 2)
            );
        }

        private static DateTime MaxDateTime(DateTime a, DateTime b) => a > b ? a : b;
        private static DateTime MinDateTime(DateTime a, DateTime b) => a < b ? a : b;
        public static async Task ApplyPayrollLockOverrideAsync(HrmsDbContext context, List<AttendanceResponseDto> records)
        {
            if (!records.Any()) return;

            var approvedPeriods = await context.PayrollPeriods
                .Where(p => p.Status == "Approved" || p.Status == "Closed")
                .Select(p => new { p.PeriodId, p.StartDate, p.EndDate })
                .ToListAsync();

            if (!approvedPeriods.Any()) return;

            var employeeIds = records.Select(r => r.EmployeeId).Distinct().ToList();
            var periodIds = approvedPeriods.Select(p => p.PeriodId).ToList();

            var approvedPairs = await context.PayrollRecords
                .Where(r => periodIds.Contains(r.PeriodId) && employeeIds.Contains(r.EmployeeId))
                .Select(r => new { r.PeriodId, r.EmployeeId })
                .ToListAsync();

            foreach (var rec in records.Where(r => r.IsLocked != true))
            {
                var inApproved = approvedPeriods.Any(p =>
                    p.StartDate <= rec.AttendanceDate && p.EndDate >= rec.AttendanceDate &&
                    approvedPairs.Any(e => e.PeriodId == p.PeriodId && e.EmployeeId == rec.EmployeeId));
                if (inApproved) rec.IsLocked = true;
            }
        }

        public static async Task<bool> IsPayrollLockedAsync(HrmsDbContext context, int employeeId, DateOnly date)
        {
            return await context.PayrollPeriods
                .AnyAsync(p => (p.Status == "Approved" || p.Status == "Closed") &&
                               p.StartDate <= date && p.EndDate >= date &&
                               context.PayrollRecords.Any(r => r.PeriodId == p.PeriodId && r.EmployeeId == employeeId));
        }
    }
}
