using HRManagement.Authorization;
using HRManagement.DTOs;
using HRManagement.Services.CurrentUsers;
using HRManagement.Services.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Controllers
{
    /// <summary>
    /// Employment contracts. HR (Employee.Manage) manages everyone's; other users can read their own.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeeContractController : ControllerBase
    {
        private const string ManagePolicy = RequirePermissionAttribute.PolicyPrefix + "Employee.Manage";

        private readonly IEmployeeContractService _contracts;
        private readonly IAuthorizationService _authorization;
        private readonly ICurrentUserService _currentUser;

        public EmployeeContractController(IEmployeeContractService contracts, IAuthorizationService authorization, ICurrentUserService currentUser)
        {
            _contracts = contracts;
            _authorization = authorization;
            _currentUser = currentUser;
        }

        private async Task<bool> CanReadAsync(int employeeId)
        {
            if ((await _authorization.AuthorizeAsync(User, ManagePolicy)).Succeeded)
                return true;
            return int.TryParse(User.FindFirst("employeeId")?.Value, out var own) && own == employeeId;
        }

        [HttpGet("employee/{employeeId:int}")]
        public async Task<IActionResult> GetByEmployee(int employeeId)
        {
            if (!await CanReadAsync(employeeId))
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Bạn không có quyền xem hợp đồng của nhân viên này." });
            return Ok(await _contracts.GetByEmployeeAsync(employeeId));
        }

        [HttpGet("expiring")]
        [RequirePermission("Employee.Manage")]
        public async Task<IActionResult> GetExpiring([FromQuery] int days = 30)
        {
            if (days < 1 || days > 365)
                return BadRequest(new { message = "Số ngày phải từ 1 đến 365." });
            return Ok(await _contracts.GetExpiringAsync(days));
        }

        [HttpPost]
        [RequirePermission("Employee.Manage")]
        public async Task<IActionResult> Create([FromBody] EmployeeContractWriteDto dto) =>
            Ok(await _contracts.CreateAsync(dto, _currentUser.GetCurrentUserId()));

        [HttpPut("{id:int}")]
        [RequirePermission("Employee.Manage")]
        public async Task<IActionResult> Update(int id, [FromBody] EmployeeContractWriteDto dto) =>
            Ok(await _contracts.UpdateAsync(id, dto));

        [HttpPut("{id:int}/terminate")]
        [RequirePermission("Employee.Manage")]
        public async Task<IActionResult> Terminate(int id, [FromBody] TerminateContractDto dto) =>
            Ok(await _contracts.TerminateAsync(id, dto.EndDate));
    }

    public class TerminateContractDto
    {
        public DateOnly EndDate { get; set; }
    }
}
