using HRManagement.Authorization;
using HRManagement.DTOs.Attendances;
using HRManagement.Models;
using HRManagement.Services.Attendances;
using HRManagement.Services.CurrentUsers;
using HRManagement.Services.FaceVerifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AttendanceController : Controller
    {
        private readonly IAttendanceService _attendanceService;
        private readonly HrmsDbContext _context;
        private readonly ICurrentUserService _currentUserService;
        private readonly IFaceVerificationService _faceVerificationService;

        public AttendanceController(IAttendanceService attendanceService, HrmsDbContext context, ICurrentUserService currentUserService, IFaceVerificationService faceVerificationService)
        {
            _attendanceService = attendanceService;
            _context = context;
            _currentUserService = currentUserService;
            _faceVerificationService = faceVerificationService;
        }




        [HttpPost("checkin")]
        [Authorize]
        public async Task<IActionResult> FaceCheckIn([FromBody] CheckInRequestDto request)
        {
            try
            {
                var employeeId = await _currentUserService.GetCurrentEmployeeIdAsync();
                
                // Add GPS validation logic here or move to service
                // For now, keep the location validation in controller as it uses SystemSettings
                await ValidateLocationAsync(request);

                var result = await _attendanceService.CheckInAsync(employeeId, request);
                return Ok(new
                {
                    message = "Check-in thành công.",
                    result.AttendanceId,
                    result.EmployeeId,
                    result.AttendanceDate,
                    result.CheckInTime,
                    // Note: Face verification results are handled inside Service
                });
            }
            catch (InvalidOperationException ex)
            {
                // Handle "Already checked in" gracefully
                if (ex.Message.Contains("đã check-in hôm nay rồi"))
                {
                    var employeeId = await _currentUserService.GetCurrentEmployeeIdAsync();
                    var today = DateOnly.FromDateTime(AppTime.Now);
                    var attendance = await _context.AttendanceRecords
                        .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.AttendanceDate == today);
                    
                    return Ok(new
                    {
                        message = "Bạn đã check-in trước đó. Hệ thống giữ nguyên thời gian đầu tiên.",
                        attendance?.AttendanceId,
                        attendance?.EmployeeId,
                        attendance?.AttendanceDate,
                        attendance?.CheckInTime
                    });
                }
                return BadRequest(new { message = ex.Message });
            }
        }

        private async Task ValidateLocationAsync(CheckInRequestDto request)
        {
             // Verify Location if configured in SystemSettings
            var officeLatSetting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == "OfficeLatitude");
            var officeLngSetting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == "OfficeLongitude");
            var radiusSetting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == "AttendanceAllowedRadius");

            if (officeLatSetting != null && officeLngSetting != null && radiusSetting != null && 
                !string.IsNullOrEmpty(officeLatSetting.SettingValue) && !string.IsNullOrEmpty(officeLngSetting.SettingValue))
            {
                if (double.TryParse(officeLatSetting.SettingValue, out var officeLat) && 
                    double.TryParse(officeLngSetting.SettingValue, out var officeLng) && 
                    double.TryParse(radiusSetting.SettingValue, out var radius))
                {
                    if (request.Latitude.HasValue && request.Longitude.HasValue)
                    {
                        var distance = CalculateDistance(request.Latitude.Value, request.Longitude.Value, officeLat, officeLng);
                        if (distance > radius)
                        {
                            request.Location = "[INVALID] " + request.Location;
                        }
                    }
                    else
                    {
                        request.Location = "[INVALID] Không có dữ liệu GPS";
                    }
                }
            }
        }

        [HttpPost("checkout")]
        [Authorize]
        public async Task<IActionResult> FaceCheckOut([FromBody] CheckOutRequestDto request)
        {
            try
            {
                var employeeId = await _currentUserService.GetCurrentEmployeeIdAsync();

                // Validation GPS using common helper
                var checkInRequest = new CheckInRequestDto 
                { 
                    Latitude = request.Latitude, 
                    Longitude = request.Longitude, 
                    Location = request.Location 
                };
                await ValidateLocationAsync(checkInRequest);
                request.Location = checkInRequest.Location;

                var result = await _attendanceService.CheckOutAsync(employeeId, request);

                return Ok(new
                {
                    message = "Check-out thành công.",
                    result.AttendanceId,
                    result.EmployeeId,
                    result.AttendanceDate,
                    result.CheckInTime,
                    result.CheckOutTime,
                    result.WorkingHours
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("my-today")]
        [Authorize]
        public async Task<IActionResult> GetMyToday()
        {
            try
            {
                var employeeId = await _currentUserService.GetCurrentEmployeeIdAsync();
                var result = await _attendanceService.GetMyTodayAsync(employeeId);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("my-history")]
        [Authorize]
        public async Task<IActionResult> GetMyHistory(
            [FromQuery] DateOnly? fromDate,
            [FromQuery] DateOnly? toDate)
        {
            try
            {
                if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
                    return BadRequest(new { message = "Từ ngày không được lớn hơn đến ngày." });

                var employeeId = await _currentUserService.GetCurrentEmployeeIdAsync();
                var result = await _attendanceService.GetMyHistoryAsync(employeeId, fromDate, toDate);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // =========================
        // MANAGEMENT ATTENDANCE
        // =========================

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetByDate([FromQuery] DateOnly date)
        {
            try
            {
                var result = await _attendanceService.GetAttendanceByDateAsync(date);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("search")]
        [Authorize]
        public async Task<IActionResult> Search(
            [FromQuery] DateOnly? fromDate,
            [FromQuery] DateOnly? toDate,
            [FromQuery] int? employeeId,
            [FromQuery] string? status)
        {
            try
            {
                if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
                    return BadRequest(new { message = "Từ ngày không được lớn hơn đến ngày." });

                // The whole company's attendance is loaded in one response, so keep the window bounded
                if (fromDate.HasValue && toDate.HasValue && toDate.Value.DayNumber - fromDate.Value.DayNumber > 366)
                    return BadRequest(new { message = "Chỉ tìm kiếm được tối đa 1 năm mỗi lần." });

                if (employeeId.HasValue && employeeId.Value <= 0)
                    return BadRequest(new { message = "EmployeeId không hợp lệ." });

                if (status != null && string.IsNullOrWhiteSpace(status))
                    return BadRequest(new { message = "Status không hợp lệ." });

                var result = await _attendanceService.SearchAttendanceAsync(fromDate, toDate, employeeId, status);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Same search, one page at a time; a manager (MANAGE without ADMIN or HR) only gets their direct reports.
        /// </summary>
        [HttpGet("search-page")]
        [Authorize]
        public async Task<IActionResult> SearchPage(
            [FromQuery] DateOnly? fromDate,
            [FromQuery] DateOnly? toDate,
            [FromQuery] int? employeeId,
            [FromQuery] string? status,
            [FromQuery] string? explanationStatus,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 15)
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
                return BadRequest(new { message = "Từ ngày không được lớn hơn đến ngày." });

            if (fromDate.HasValue && toDate.HasValue && toDate.Value.DayNumber - fromDate.Value.DayNumber > 366)
                return BadRequest(new { message = "Chỉ tìm kiếm được tối đa 1 năm mỗi lần." });

            if (employeeId.HasValue && employeeId.Value <= 0)
                return BadRequest(new { message = "EmployeeId không hợp lệ." });

            if (page < 1 || pageSize < 1 || pageSize > 200)
                return BadRequest(new { message = "Trang không hợp lệ (pageSize tối đa 200)." });

            int? managerEmployeeId = null;
            if (User.IsInRole("MANAGE") && !User.IsInRole("ADMIN") && !User.IsInRole("HR"))
                managerEmployeeId = await _currentUserService.GetCurrentEmployeeIdAsync();

            var result = await _attendanceService.SearchAttendancePageAsync(
                fromDate, toDate, employeeId, status, explanationStatus, managerEmployeeId, page, pageSize);
            return Ok(result);
        }

        [HttpGet("{employeeId:int}/{date}")]
        [Authorize]
        public async Task<IActionResult> GetDetail(int employeeId, DateOnly date)
        {
            try
            {
                if (employeeId <= 0)
                    return BadRequest(new { message = "EmployeeId không hợp lệ." });

                var result = await _attendanceService.GetAttendanceDetailAsync(employeeId, date);

                if (result == null)
                    return NotFound(new { message = "Không tìm thấy dữ liệu chấm công." });

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("manual-adjust/{attendanceId:int}")]
        [Authorize]
        public async Task<IActionResult> ManualAdjust(int attendanceId, [FromBody] ManualAdjustAttendanceDto dto)
        {
            try
            {
                if (attendanceId <= 0)
                    return BadRequest(new { message = "AttendanceId không hợp lệ." });

                var approverId = _currentUserService.GetCurrentUserId();
                var result = await _attendanceService.ManualAdjustAttendanceAsync(attendanceId, approverId, dto);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [HttpPost("manual-create")]
        [Authorize]
        public async Task<IActionResult> ManualCreate([FromBody] ManualCreateAttendanceDto dto)
        {
            try
            {
                var approverId = _currentUserService.GetCurrentUserId();
                var result = await _attendanceService.ManualCreateAttendanceAsync(approverId, dto);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [HttpPut("{attendanceId:int}/lock")]
        [RequirePermission("Attendance.Manage")]
        public async Task<IActionResult> Lock(int attendanceId)
        {
            try
            {
                if (attendanceId <= 0)
                    return BadRequest(new { message = "AttendanceId không hợp lệ." });

                var userId = _currentUserService.GetCurrentUserId();
                await _attendanceService.LockAttendanceAsync(attendanceId, userId);

                return Ok(new { message = "Khóa chấm công thành công." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [HttpPut("{attendanceId:int}/unlock")]
        [RequirePermission("Attendance.Manage")]
        public async Task<IActionResult> Unlock(int attendanceId)
        {
            try
            {
                if (attendanceId <= 0)
                    return BadRequest(new { message = "AttendanceId không hợp lệ." });

                var userId = _currentUserService.GetCurrentUserId();
                await _attendanceService.UnlockAttendanceAsync(attendanceId, userId);

                return Ok(new { message = "Mở khóa chấm công thành công." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [HttpGet("logs")]
        [Authorize]
        public async Task<IActionResult> GetLogs([FromQuery] int employeeId, [FromQuery] DateOnly date)
        {
            try
            {
                if (employeeId <= 0)
                    return BadRequest(new { message = "EmployeeId không hợp lệ." });

                var result = await _attendanceService.GetLogsAsync(employeeId, date);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{attendanceId:int}/submit-explanation")]
        [Authorize]
        public async Task<IActionResult> SubmitExplanation(int attendanceId, [FromBody] SubmitExplanationDto dto)
        {
            var employeeId = await _currentUserService.GetCurrentEmployeeIdAsync();
            var result = await _attendanceService.SubmitExplanationAsync(employeeId, attendanceId, dto);
            return Ok(new { message = "Đã gửi phiếu giải trình. Đang chờ Quản lý duyệt.", data = result });
        }

        [HttpPost("submit-absent-explanation")]
        [Authorize]
        public async Task<IActionResult> SubmitAbsentExplanation([FromBody] SubmitAbsentExplanationDto dto)
        {
            var employeeId = await _currentUserService.GetCurrentEmployeeIdAsync();
            var result = await _attendanceService.SubmitAbsentExplanationAsync(employeeId, dto.Date, dto);
            return Ok(new { message = "Đã gửi phiếu giải trình cho ngày vắng mặt. Đang chờ Quản lý duyệt.", data = result });
        }

        [HttpPut("{attendanceId:int}/approve-explanation")]
        [Authorize]
        public async Task<IActionResult> ApproveExplanation(int attendanceId, [FromBody] ApproveExplanationDto dto)
        {
            var managerId = _currentUserService.GetCurrentUserId();
            var result = await _attendanceService.ApproveExplanationAsync(managerId, attendanceId, dto);
            return Ok(new { message = "Đã xử lý phiếu giải trình.", data = result });
        }

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371e3; // metres
            var phi1 = lat1 * Math.PI / 180;
            var phi2 = lat2 * Math.PI / 180;
            var deltaPhi = (lat2 - lat1) * Math.PI / 180;
            var deltaLambda = (lon2 - lon1) * Math.PI / 180;

            var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                    Math.Cos(phi1) * Math.Cos(phi2) *
                    Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c; // in metres
        }
    }
}
