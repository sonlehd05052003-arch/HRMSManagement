using HRManagement.Authorization;
using HRManagement.Services.Reports;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Controllers
{
    /// <summary>HR reports of the signed-in company: headcount movements, attendance and payroll cost.</summary>
    [ApiController]
    [Route("api/reports")]
    [RequirePermission("Reports.View")]
    public class ReportsController : ControllerBase
    {
        private readonly IReportService _reportService;

        public ReportsController(IReportService reportService)
        {
            _reportService = reportService;
        }

        private static string? Invalid(int year, int? month)
        {
            if (year < 2000 || year > AppTime.Today.Year + 1) return "Năm không hợp lệ.";
            if (month is < 1 or > 12) return "Tháng không hợp lệ.";
            return null;
        }

        [HttpGet("workforce")]
        public async Task<IActionResult> Workforce([FromQuery] int year, [FromQuery] int? departmentId)
        {
            if (Invalid(year, null) is { } error) return BadRequest(new { message = error });
            return Ok(await _reportService.GetWorkforceAsync(year, departmentId));
        }

        [HttpGet("attendance")]
        public async Task<IActionResult> Attendance([FromQuery] int year, [FromQuery] int month, [FromQuery] int? departmentId)
        {
            if (Invalid(year, month) is { } error) return BadRequest(new { message = error });
            return Ok(await _reportService.GetAttendanceAsync(year, month, departmentId));
        }

        [HttpGet("payroll")]
        public async Task<IActionResult> Payroll([FromQuery] int year, [FromQuery] int? month)
        {
            if (Invalid(year, month) is { } error) return BadRequest(new { message = error });
            return Ok(await _reportService.GetPayrollAsync(year, month));
        }

        /// <summary>One of the reports above as an .xlsx file</summary>
        [HttpGet("{kind}/excel")]
        public async Task<IActionResult> Excel(string kind, [FromQuery] int year, [FromQuery] int? month, [FromQuery] int? departmentId)
        {
            if (Invalid(year, month) is { } error) return BadRequest(new { message = error });
            if (kind is not ("workforce" or "attendance" or "payroll")) return NotFound();
            if (kind == "attendance" && month == null) return BadRequest(new { message = "Chọn tháng." });

            var bytes = await _reportService.ExportAsync(kind, year, month, departmentId);
            var name = kind switch { "workforce" => "bien-dong-nhan-su", "attendance" => "cham-cong", _ => "chi-phi-luong" };
            var suffix = month.HasValue && kind != "workforce" ? $"{year}-{month:D2}" : $"{year}";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"bao-cao-{name}-{suffix}.xlsx");
        }
    }
}
