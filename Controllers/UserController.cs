using HRManagement.Authorization;
using HRManagement.DTOs;
using HRManagement.Services.CurrentUsers;
using HRManagement.Services.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // The SuperAdmin manages a chosen company's accounts too (X-Company-Id header)
    [SuperAdminCompanyScope]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ICurrentUserService _currentUserService;

        public UserController(IUserService userService, ICurrentUserService currentUserService)
        {
            _userService = userService;
            _currentUserService = currentUserService;
        }

        [RequirePermission("User.View")]
        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _userService.GetAllUsersAsync();
            return Ok(users);
        }

        /// <summary>Employees an account can be linked to, for the create / edit account forms.</summary>
        [RequirePermission("User.Manage")]
        [HttpGet("employee-options")]
        public async Task<IActionResult> GetEmployeeOptions()
        {
            return Ok(await _userService.GetEmployeeOptionsAsync());
        }

        [RequirePermission("User.View")]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUser(int id)
        {
            var user = await _userService.GetUserAsync(id);
            return user is null ? NotFound() : Ok(user);
        }

        [RequirePermission("User.Manage")]
        [HttpPost]
        public async Task<IActionResult> CreateUser(CreateUserDTO dto)
        {
            var (success, error, username) = await _userService.CreateUserAsync(dto);
            if (!success)
                return BadRequest(error);

            return Ok(new { message = "User created successfully", username });
        }

        [RequirePermission("User.Manage")]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, UpdateUserDTO dto)
        {
            var (success, error) = await _userService.UpdateUserAsync(id, dto);
            if (error is null && !success)
                return NotFound();
            if (!success)
                return BadRequest(error);

            return Ok("User updated successfully");
        }

        [RequirePermission("User.Manage")]
        [HttpPatch("{id}/deactivate")]
        public async Task<IActionResult> DeactivateUser(int id)
        {
            var (success, _) = await _userService.DeactivateUserAsync(id);
            return success ? Ok("User deactivated") : NotFound();
        }

        [RequirePermission("User.Manage")]
        [HttpPatch("{id}/activate")]
        public async Task<IActionResult> ActivateUser(int id)
        {
            var (success, error) = await _userService.ActivateUserAsync(id);
            if (error is null && !success)
                return NotFound();
            if (!success)
                return BadRequest(error);

            return Ok("User activated");
        }

        /// <summary>Sets a new password chosen by the administrator and signs out the account's sessions.</summary>
        [RequirePermission("User.Manage")]
        [HttpPost("{id}/reset-password")]
        public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetUserPasswordDTO dto)
        {
            var canResetAdmins = User.IsInRole(PlatformRoles.SuperAdmin) || User.IsInRole("ADMIN");
            var (success, error) = await _userService.ResetPasswordAsync(id, dto, _currentUserService.GetUserId(), canResetAdmins);
            if (error is null && !success)
                return NotFound();
            if (!success)
                return BadRequest(new { message = error });

            return Ok(new { message = "Đã đặt lại mật khẩu." });
        }
    }
}
