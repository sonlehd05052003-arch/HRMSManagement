using HRManagement.Authorization;
using HRManagement.DTOs.SystemSettings;
using HRManagement.Models;
using HRManagement.Services.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SystemSettingsController : ControllerBase
    {
        private readonly HrmsDbContext _context;
        private readonly ITenantContext _tenant;

        public SystemSettingsController(HrmsDbContext context, ITenantContext tenant)
        {
            _context = context;
            _tenant = tenant;
        }

        [HttpGet("location")]
        [Authorize]
        public async Task<IActionResult> GetLocationSettings()
        {
            var settings = await _context.SystemSettings
                .Where(s => s.SettingKey == "OfficeLatitude" || s.SettingKey == "OfficeLongitude" ||
                            s.SettingKey == "AttendanceAllowedRadius" || s.SettingKey == "CheckInMethod" ||
                            s.SettingKey == "AllowedIpAddresses")
                .ToListAsync();

            var dto = new LocationSettingsDto();
            foreach (var s in settings)
            {
                if (s.SettingKey == "OfficeLatitude" && double.TryParse(s.SettingValue, out var lat)) dto.OfficeLatitude = lat;
                if (s.SettingKey == "OfficeLongitude" && double.TryParse(s.SettingValue, out var lng)) dto.OfficeLongitude = lng;
                if (s.SettingKey == "AttendanceAllowedRadius" && double.TryParse(s.SettingValue, out var rad)) dto.AttendanceAllowedRadius = rad;
                if (s.SettingKey == "CheckInMethod") dto.CheckInMethod = s.SettingValue ?? "Location";
                if (s.SettingKey == "AllowedIpAddresses") dto.AllowedIpAddresses = s.SettingValue;
            }

            return Ok(dto);
        }

        [HttpPut("location")]
        [RequirePermission("SystemSettings.ManageGeneral")]
        public async Task<IActionResult> UpdateLocationSettings([FromBody] LocationSettingsDto dto)
        {
            await UpdateOrInsertSetting("OfficeLatitude", dto.OfficeLatitude.ToString(), "Attendance");
            await UpdateOrInsertSetting("OfficeLongitude", dto.OfficeLongitude.ToString(), "Attendance");
            await UpdateOrInsertSetting("AttendanceAllowedRadius", dto.AttendanceAllowedRadius.ToString(), "Attendance");
            await UpdateOrInsertSetting("CheckInMethod", dto.CheckInMethod ?? "Location", "Attendance");
            await UpdateOrInsertSetting("AllowedIpAddresses", dto.AllowedIpAddresses ?? "", "Attendance");

            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật cấu hình vị trí điểm danh thành công." });
        }

        [HttpGet("approval")]
        [Authorize]
        public async Task<IActionResult> GetApprovalSettings()
        {
            var settings = await _context.SystemSettings
                .Where(s => s.SettingKey == "Approval.TopLevelFallbackUserId" || s.SettingKey == "Approval.DefaultFallbackUserId")
                .ToListAsync();

            var dto = new ApprovalSettingsDto();
            foreach (var s in settings)
            {
                if (s.SettingKey == "Approval.TopLevelFallbackUserId" && int.TryParse(s.SettingValue, out var topId))
                    dto.TopLevelFallbackUserId = topId;
                if (s.SettingKey == "Approval.DefaultFallbackUserId" && int.TryParse(s.SettingValue, out var defId))
                    dto.DefaultFallbackUserId = defId;
            }

            return Ok(dto);
        }

        [HttpPut("approval")]
        [RequirePermission("SystemSettings.ManageGeneral")]
        public async Task<IActionResult> UpdateApprovalSettings([FromBody] ApprovalSettingsDto dto)
        {
            await UpdateOrInsertSetting("Approval.TopLevelFallbackUserId", dto.TopLevelFallbackUserId?.ToString() ?? "", "Workflow");
            await UpdateOrInsertSetting("Approval.DefaultFallbackUserId", dto.DefaultFallbackUserId?.ToString() ?? "", "Workflow");
            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật cấu hình phê duyệt thành công." });
        }

        [HttpGet("payroll")]
        [Authorize]
        public async Task<IActionResult> GetPayrollSettings()
        {
            var settings = await _context.SystemSettings
                .Where(s => s.SettingKey == "Payroll.CutOffDay" || s.SettingKey == "Payroll.DefaultReviewWindowDays")
                .ToListAsync();

            var cutOff  = settings.FirstOrDefault(s => s.SettingKey == "Payroll.CutOffDay");
            var review  = settings.FirstOrDefault(s => s.SettingKey == "Payroll.DefaultReviewWindowDays");

            var dto = new PayrollSettingsDto
            {
                PayrollCutOffDay        = cutOff != null && int.TryParse(cutOff.SettingValue, out var day) ? day : 1,
                DefaultReviewWindowDays = review != null && int.TryParse(review.SettingValue, out var rw)  ? rw  : 5,
            };

            return Ok(dto);
        }

        [HttpPut("payroll")]
        [RequirePermission("SystemSettings.ManagePayroll")]
        public async Task<IActionResult> UpdatePayrollSettings([FromBody] PayrollSettingsDto dto)
        {
            if (dto.PayrollCutOffDay < 1 || dto.PayrollCutOffDay > 28)
                return BadRequest(new { message = "Ngày chốt lương phải từ 1 đến 28." });

            if (dto.DefaultReviewWindowDays < 1 || dto.DefaultReviewWindowDays > 30)
                return BadRequest(new { message = "Số ngày review phải từ 1 đến 30." });

            await UpdateOrInsertSetting("Payroll.CutOffDay",                dto.PayrollCutOffDay.ToString(),        "Payroll");
            await UpdateOrInsertSetting("Payroll.DefaultReviewWindowDays",  dto.DefaultReviewWindowDays.ToString(), "Payroll");
            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật cấu hình kỳ lương thành công." });
        }

        [HttpGet("payroll-calculation")]
        [Authorize]
        public async Task<IActionResult> GetPayrollCalculationSettings()
        {
            var keys = new[]
            {
                "Payroll.Calc.BhxhRate", "Payroll.Calc.BhytRate", "Payroll.Calc.BhtnRate",
                "Payroll.Calc.InsuranceCap", "Payroll.Calc.InsuranceBaseMode", "Payroll.Calc.InsuranceFixedBase",
                "Payroll.Calc.PersonalDeduction", "Payroll.Calc.DependentDeduction",
                "Payroll.Calc.OtWeekdayMultiplier", "Payroll.Calc.OtWeekendMultiplier", "Payroll.Calc.OtHolidayMultiplier"
            };
            var settings = await _context.SystemSettings
                .Where(s => keys.Contains(s.SettingKey))
                .ToListAsync();

            var dto = new PayrollCalculationSettingsDto(); // defaults already set
            foreach (var s in settings)
            {
                if (s.SettingKey == "Payroll.Calc.BhxhRate"              && decimal.TryParse(s.SettingValue, out var v1))  dto.BhxhRate             = v1;
                if (s.SettingKey == "Payroll.Calc.BhytRate"              && decimal.TryParse(s.SettingValue, out var v2))  dto.BhytRate             = v2;
                if (s.SettingKey == "Payroll.Calc.BhtnRate"              && decimal.TryParse(s.SettingValue, out var v3))  dto.BhtnRate             = v3;
                if (s.SettingKey == "Payroll.Calc.InsuranceCap"          && decimal.TryParse(s.SettingValue, out var v4))  dto.InsuranceCap         = v4;
                if (s.SettingKey == "Payroll.Calc.InsuranceBaseMode")                                                       dto.InsuranceBaseMode    = s.SettingValue ?? "Gross";
                if (s.SettingKey == "Payroll.Calc.InsuranceFixedBase"    && decimal.TryParse(s.SettingValue, out var v6))  dto.InsuranceFixedBase   = v6;
                if (s.SettingKey == "Payroll.Calc.PersonalDeduction"     && decimal.TryParse(s.SettingValue, out var v7))  dto.PersonalDeduction    = v7;
                if (s.SettingKey == "Payroll.Calc.DependentDeduction"    && decimal.TryParse(s.SettingValue, out var v8))  dto.DependentDeduction   = v8;
                if (s.SettingKey == "Payroll.Calc.OtWeekdayMultiplier"   && decimal.TryParse(s.SettingValue, out var v9))  dto.OtWeekdayMultiplier  = v9;
                if (s.SettingKey == "Payroll.Calc.OtWeekendMultiplier"   && decimal.TryParse(s.SettingValue, out var v10)) dto.OtWeekendMultiplier  = v10;
                if (s.SettingKey == "Payroll.Calc.OtHolidayMultiplier"   && decimal.TryParse(s.SettingValue, out var v11)) dto.OtHolidayMultiplier  = v11;
            }
            return Ok(dto);
        }

        [HttpPut("payroll-calculation")]
        [RequirePermission("SystemSettings.ManageAdvanced")]
        public async Task<IActionResult> UpdatePayrollCalculationSettings([FromBody] PayrollCalculationSettingsDto dto)
        {
            if (dto.BhxhRate < 0 || dto.BhxhRate > 100)         return BadRequest(new { message = "Tỷ lệ BHXH không hợp lệ." });
            if (dto.BhytRate < 0 || dto.BhytRate > 100)         return BadRequest(new { message = "Tỷ lệ BHYT không hợp lệ." });
            if (dto.BhtnRate < 0 || dto.BhtnRate > 100)         return BadRequest(new { message = "Tỷ lệ BHTN không hợp lệ." });
            if (dto.InsuranceCap < 0)                            return BadRequest(new { message = "Mức trần bảo hiểm không hợp lệ." });
            if (dto.InsuranceFixedBase < 0)                      return BadRequest(new { message = "Mức lương căn cứ cố định không hợp lệ." });
            if (dto.PersonalDeduction < 0)                       return BadRequest(new { message = "Giảm trừ bản thân không hợp lệ." });
            if (dto.DependentDeduction < 0)                      return BadRequest(new { message = "Giảm trừ người phụ thuộc không hợp lệ." });
            if (dto.OtWeekdayMultiplier <= 0)                    return BadRequest(new { message = "Hệ số OT ngày thường phải lớn hơn 0." });
            if (dto.OtWeekendMultiplier <= 0)                    return BadRequest(new { message = "Hệ số OT cuối tuần phải lớn hơn 0." });
            if (dto.OtHolidayMultiplier <= 0)                    return BadRequest(new { message = "Hệ số OT ngày lễ phải lớn hơn 0." });

            await UpdateOrInsertSetting("Payroll.Calc.BhxhRate",              dto.BhxhRate.ToString(),              "Payroll");
            await UpdateOrInsertSetting("Payroll.Calc.BhytRate",              dto.BhytRate.ToString(),              "Payroll");
            await UpdateOrInsertSetting("Payroll.Calc.BhtnRate",              dto.BhtnRate.ToString(),              "Payroll");
            await UpdateOrInsertSetting("Payroll.Calc.InsuranceCap",          dto.InsuranceCap.ToString(),          "Payroll");
            await UpdateOrInsertSetting("Payroll.Calc.InsuranceBaseMode",     dto.InsuranceBaseMode,                "Payroll");
            await UpdateOrInsertSetting("Payroll.Calc.InsuranceFixedBase",    dto.InsuranceFixedBase.ToString(),    "Payroll");
            await UpdateOrInsertSetting("Payroll.Calc.PersonalDeduction",     dto.PersonalDeduction.ToString(),     "Payroll");
            await UpdateOrInsertSetting("Payroll.Calc.DependentDeduction",    dto.DependentDeduction.ToString(),    "Payroll");
            await UpdateOrInsertSetting("Payroll.Calc.OtWeekdayMultiplier",   dto.OtWeekdayMultiplier.ToString(),   "Payroll");
            await UpdateOrInsertSetting("Payroll.Calc.OtWeekendMultiplier",   dto.OtWeekendMultiplier.ToString(),   "Payroll");
            await UpdateOrInsertSetting("Payroll.Calc.OtHolidayMultiplier",   dto.OtHolidayMultiplier.ToString(),   "Payroll");
            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật cấu hình tính lương thành công." });
        }

        [HttpGet("company")]
        [Authorize]
        public async Task<IActionResult> GetCompanySettings()
        {
            var company = await FindCurrentCompanyAsync();
            if (company is null)
                return NotFound(new { message = "Tài khoản không thuộc công ty nào." });

            return Ok(CompanySettingsDto.FromCompany(company));
        }

        // Company info lives on the Companies row (shared with the SuperAdmin screen); the company code
        // and active status stay under the SuperAdmin's control.
        [HttpPut("company")]
        [RequirePermission("SystemSettings.ManageAdvanced")]
        public async Task<IActionResult> UpdateCompanySettings([FromBody] CompanySettingsDto dto)
        {
            var company = await FindCurrentCompanyAsync();
            if (company is null)
                return NotFound(new { message = "Tài khoản không thuộc công ty nào." });

            if (string.IsNullOrWhiteSpace(dto.CompanyName))
                return BadRequest(new { message = "Tên công ty là bắt buộc." });

            company.CompanyName  = dto.CompanyName.Trim();
            company.Address      = dto.Address?.Trim();
            company.Phone        = dto.Phone?.Trim();
            company.Email        = dto.Email?.Trim();
            if (dto.TaxCode != null) company.TaxCode = dto.TaxCode.Trim();
            company.ModifiedDate = AppTime.Now;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật thông tin công ty thành công." });
        }

        private Task<Company?> FindCurrentCompanyAsync() =>
            _context.Companies.FirstOrDefaultAsync(c => c.CompanyId == _tenant.CompanyId);

        private async Task UpdateOrInsertSetting(string key, string value, string category)
        {
            var exists = await _context.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == key);
            int? modifiedBy = null;
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var idStr = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(idStr, out var id)) modifiedBy = id;
            }

            if (exists == null)
            {
                _context.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = key,
                    SettingValue = value,
                    SettingCategory = category,
                    ModifiedDate = AppTime.Now,
                    ModifiedBy = modifiedBy
                });
            }
            else
            {
                exists.SettingValue = value;
                exists.ModifiedDate = AppTime.Now;
                exists.ModifiedBy = modifiedBy;
            }
        }
    }
}
