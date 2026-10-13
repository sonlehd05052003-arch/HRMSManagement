using HRManagement.Authorization;
using HRManagement.DTOs;
using HRManagement.Services.CurrentUsers;
using HRManagement.Services.Holidays;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Controllers
{
    /// <summary>The company's holiday calendar: everyone can read it, ADMIN and HR maintain it.</summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class HolidayController : ControllerBase
    {
        private readonly IHolidayService _holidays;
        private readonly ICurrentUserService _currentUser;

        public HolidayController(IHolidayService holidays, ICurrentUserService currentUser)
        {
            _holidays = holidays;
            _currentUser = currentUser;
        }

        [HttpGet]
        public async Task<IActionResult> GetByYear([FromQuery] int? year) =>
            Ok(await _holidays.GetByYearAsync(year ?? AppTime.Today.Year));

        [HttpPost]
        [RequirePermission("SystemSettings.ManageAdvanced", "SystemSettings.ManagePayroll")]
        public async Task<IActionResult> Create([FromBody] HolidayWriteDto dto) =>
            Ok(await _holidays.CreateAsync(dto, _currentUser.GetCurrentUserId()));

        [HttpPut("{id:int}")]
        [RequirePermission("SystemSettings.ManageAdvanced", "SystemSettings.ManagePayroll")]
        public async Task<IActionResult> Update(int id, [FromBody] HolidayWriteDto dto) =>
            Ok(await _holidays.UpdateAsync(id, dto));

        [HttpDelete("{id:int}")]
        [RequirePermission("SystemSettings.ManageAdvanced", "SystemSettings.ManagePayroll")]
        public async Task<IActionResult> Delete(int id)
        {
            await _holidays.DeleteAsync(id);
            return NoContent();
        }

        /// <summary>Adds 1/1, 30/4, 1/5 and 2/9 of the year when missing.</summary>
        [HttpPost("fixed-vietnam/{year:int}")]
        [RequirePermission("SystemSettings.ManageAdvanced", "SystemSettings.ManagePayroll")]
        public async Task<IActionResult> AddFixedVietnam(int year) =>
            Ok(new { added = await _holidays.AddFixedVietnamHolidaysAsync(year, _currentUser.GetCurrentUserId()) });
    }
}
