using HRManagement.Authorization;
using HRManagement.DTOs.Payroll;
using HRManagement.Services.Payroll;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace HRManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PayrollController : ControllerBase
    {
        private readonly IPayrollService _payrollService;

        public PayrollController(IPayrollService payrollService)
        {
            _payrollService = payrollService;
        }

        // ══════════════════════════════════════════════
        // KỲ LƯƠNG (PERIODS)
        // ══════════════════════════════════════════════

        [HttpGet("periods")]
        [RequirePermission("Payroll.View")]
        public async Task<IActionResult> GetPeriods()
        {
            var periods = await _payrollService.GetAllPeriodsAsync();
            return Ok(periods);
        }

        [HttpPost("periods")]
        [RequirePermission("Payroll.Process")]
        public async Task<IActionResult> CreatePeriod([FromBody] CreatePayrollPeriodDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var period = await _payrollService.CreatePeriodAsync(dto);
                return CreatedAtAction(nameof(GetPeriodById), new { periodId = period.PeriodId }, period);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("periods/{periodId:int}")]
        [RequirePermission("Payroll.View")]
        public async Task<IActionResult> GetPeriodById(int periodId)
        {
            try
            {
                var period = await _payrollService.GetPeriodByIdAsync(periodId);
                return Ok(period);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpGet("periods/{periodId:int}/summary")]
        [RequirePermission("Payroll.View")]
        public async Task<IActionResult> GetPeriodSummary(int periodId)
        {
            try
            {
                var summary = await _payrollService.GetPeriodSummaryAsync(periodId);
                return Ok(summary);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpPost("periods/{periodId:int}/calculate")]
        [RequirePermission("Payroll.Process")]
        public async Task<IActionResult> CalculateAll(int periodId)
        {
            var results = await _payrollService.CalculateForAllEmployeesAsync(periodId);
            return Ok(results);
        }

        [HttpPut("lock-approved-attendance")]
        [RequirePermission("Payroll.Approve")]
        public async Task<IActionResult> LockApprovedAttendance()
        {
            var count = await _payrollService.LockAttendanceForAllApprovedPeriodsAsync();
            return Ok(new { message = $"Đã khóa {count} bản ghi chấm công thuộc các kỳ lương đã phê duyệt." });
        }

        [HttpPut("periods/{periodId:int}/reject")]
        [RequirePermission("Payroll.Approve")]
        public async Task<IActionResult> RejectPeriod(int periodId, [FromBody] RejectPayrollPeriodDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdString, out int userId)) return Unauthorized();

                var period = await _payrollService.RejectPeriodAsync(periodId, userId, dto.Reason);
                return Ok(period);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpPut("periods/{periodId:int}/approve")]
        [RequirePermission("Payroll.Approve")]
        public async Task<IActionResult> ApprovePeriod(int periodId)
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdString, out int userId)) return Unauthorized();

            var period = await _payrollService.ApprovePeriodAsync(periodId, userId);
            return Ok(period);
        }

        // ══════════════════════════════════════════════
        // BẢNG LƯƠNG (RECORDS)
        // ══════════════════════════════════════════════

        [HttpGet("records/{periodId:int}")]
        [RequirePermission("Payroll.View")]
        public async Task<IActionResult> GetRecordsByPeriod(int periodId)
        {
            if (User.IsInRole("MANAGE"))
            {
                var employeeIdClaim = User.FindFirst("employeeId")?.Value;
                if (int.TryParse(employeeIdClaim, out var managerEmployeeId))
                {
                    var records = await _payrollService.GetRecordsByPeriodAsync(periodId, managerEmployeeId);
                    return Ok(records);
                }
            }

            var allRecords = await _payrollService.GetRecordsByPeriodAsync(periodId);
            return Ok(allRecords);
        }

        [HttpGet("records/detail/{recordId:int}")]
        [RequirePermission("Payroll.View")]
        public async Task<IActionResult> GetRecord(int recordId)
        {
            try
            {
                var record = await _payrollService.GetRecordByIdAsync(recordId);
                return Ok(record);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpGet("records/employee/{employeeId:int}")]
        public async Task<IActionResult> GetRecordsByEmployee(int employeeId)
        {
            // TODO: Phân quyền để NV chỉ xem được của chính mình
            var records = await _payrollService.GetRecordsByEmployeeAsync(employeeId);
            return Ok(records);
        }

        [HttpPost("periods/{periodId:int}/calculate/{employeeId:int}")]
        [RequirePermission("Payroll.Process")]
        public async Task<IActionResult> CalculateForEmployee(int periodId, int employeeId)
        {
            var record = await _payrollService.CalculateForEmployeeAsync(employeeId, periodId);
            return Ok(record);
        }

        [HttpPut("records/{recordId:int}/bonus")]
        [RequirePermission("Payroll.Process")]
        public async Task<IActionResult> UpdateBonus(int recordId, [FromBody] decimal bonusAmount)
        {
            var record = await _payrollService.UpdateBonusAsync(recordId, bonusAmount);
            return Ok(record);
        }

        [HttpPost("records/{recordId:int}/allowance")]
        [RequirePermission("Payroll.Process")]
        public async Task<IActionResult> AddAllowance(int recordId, [FromBody] CreatePayrollAllowanceDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var record = await _payrollService.AddAllowanceAsync(recordId, dto);
            return Ok(record);
        }

        [HttpDelete("records/{recordId:int}/allowance/{allowanceId:int}")]
        [RequirePermission("Payroll.Process")]
        public async Task<IActionResult> RemoveAllowance(int recordId, int allowanceId)
        {
            var record = await _payrollService.RemoveAllowanceAsync(recordId, allowanceId);
            return Ok(record);
        }

        [HttpPost("records/{recordId:int}/deduction")]
        [RequirePermission("Payroll.Process")]
        public async Task<IActionResult> AddDeduction(int recordId, [FromBody] CreatePayrollDeductionDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var record = await _payrollService.AddDeductionAsync(recordId, dto);
            return Ok(record);
        }

        [HttpDelete("records/{recordId:int}/deduction/{deductionId:int}")]
        [RequirePermission("Payroll.Process")]
        public async Task<IActionResult> RemoveDeduction(int recordId, int deductionId)
        {
            var record = await _payrollService.RemoveDeductionAsync(recordId, deductionId);
            return Ok(record);
        }

        // ══════════════════════════════════════════════
        // PHIẾU LƯƠNG (PAYSLIPS)
        // ══════════════════════════════════════════════

        [HttpPost("payslips/period/{periodId:int}/generate-all")]
        [RequirePermission("Payroll.Process")]
        public async Task<IActionResult> GeneratePayslipsForPeriod(int periodId)
        {
            var count = await _payrollService.GeneratePayslipsForPeriodAsync(periodId);
            return Ok(new { message = $"Đã tạo {count} phiếu lương.", count });
        }

        [HttpPost("payslips/{recordId:int}/generate")]
        [RequirePermission("Payroll.Process")]
        public async Task<IActionResult> GeneratePayslip(int recordId)
        {
            var payslip = await _payrollService.GeneratePayslipAsync(recordId);
            return Ok(payslip);
        }

        [HttpGet("payslips/employee/{employeeId:int}")]
        public async Task<IActionResult> GetPayslipsByEmployee(int employeeId)
        {
            var payslips = await _payrollService.GetPayslipsByEmployeeAsync(employeeId);
            return Ok(payslips);
        }

        [HttpGet("payslips/{payslipId:int}/pdf")]
        public async Task<IActionResult> DownloadPdf(int payslipId)
        {
            var pdfBytes = await _payrollService.GetPayslipPdfAsync(payslipId);

            if (pdfBytes == null || pdfBytes.Length == 0)
            {
                return BadRequest(new { message = "Không thể tạo PDF" });
            }
            return File(pdfBytes, "application/pdf", $"payslip_{payslipId}.pdf");
        }

        // ══════════════════════════════════════════════
        // TIỆN ÍCH
        // ══════════════════════════════════════════════

        [HttpGet("export/{periodId:int}")]
        [RequirePermission("Payroll.Process")]
        public async Task<IActionResult> ExportExcel(int periodId)
        {
            var excelBytes = await _payrollService.ExportPayrollExcelAsync(periodId);
            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Payroll_Period_{periodId}.xlsx");
        }

        [HttpPost("tax/calculate")]
        public async Task<IActionResult> CalculateTax([FromBody] TaxCalculationRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var result = await _payrollService.CalculateTaxAsync(request);
            return Ok(result);
        }

        // ══════════════════════════════════════════════
        // ATTENDANCE REVIEW
        // ══════════════════════════════════════════════

        /// <summary>HR kích hoạt thủ công AttendanceReview (backup cho background service)</summary>
        [HttpPut("periods/{periodId:int}/trigger-attendance-review")]
        [RequirePermission("Payroll.Process", "Payroll.Approve")]
        public async Task<IActionResult> TriggerAttendanceReview(int periodId)
        {
            var period = await _payrollService.TriggerAttendanceReviewAsync(periodId);
            return Ok(period);
        }

        /// <summary>NV xem bảng chấm công của mình trong kỳ (AttendanceReview)</summary>
        [HttpGet("records/my/{periodId:int}/attendance")]
        [Authorize]
        public async Task<IActionResult> GetMyAttendanceSummary(int periodId)
        {
            try
            {
                var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdStr, out int userId)) return Unauthorized();
                var summary = await _payrollService.GetMyAttendanceSummaryAsync(userId, periodId);
                return Ok(summary);
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }

        /// <summary>Danh sách kỳ lương của NV hiện tại</summary>
        [HttpGet("periods/my")]
        [Authorize]
        public async Task<IActionResult> GetMyPeriods()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdStr, out int userId)) return Unauthorized();

            var periods = await _payrollService.GetPeriodsForEmployeeAsync(userId);
            return Ok(periods);
        }
    }
}
