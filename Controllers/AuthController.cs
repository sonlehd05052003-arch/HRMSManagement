using HRManagement.Authorization;
using HRManagement.DTOs;
using HRManagement.DTOs.Auths;
using HRManagement.Services.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequestDTO request)
        {
            var (success, error, token) = await _authService.LoginAsync(request);
            if (!success)
                return Unauthorized(new { message = error });

            // The token only travels in the HttpOnly cookie; the client gets the profile it needs to render
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            AuthCookie.Append(HttpContext, token!, jwt.ValidTo);

            return Ok(new { message = "Đăng nhập thành công", user = ToSessionUser(jwt.Claims) });
        }

        /// <summary>The signed-in user, read from the cookie: the client keeps nothing in browser storage and asks on load.</summary>
        [Authorize]
        [HttpGet("me")]
        public IActionResult Me() => Ok(ToSessionUser(User.Claims));

        // Raw JWT claims use short names ("role"); the validated principal maps them to ClaimTypes.*
        private static object ToSessionUser(IEnumerable<Claim> claims)
        {
            var list = claims.ToList();
            string? Find(params string[] types) => list.FirstOrDefault(c => types.Contains(c.Type))?.Value;

            return new
            {
                role = Find("role", ClaimTypes.Role),
                userId = Find("nameid", ClaimTypes.NameIdentifier),
                employeeId = int.TryParse(Find("employeeId"), out var employeeId) ? employeeId : (int?)null,
                userName = Find("unique_name", ClaimTypes.Name),
                expiresTime = long.TryParse(Find("exp"), out var exp) ? exp : 0,
                isTopLevel = Find("IsTopLevel") == "true",
                positionName = Find("PositionName") ?? ""
            };
        }

        [AllowAnonymous]
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            AuthCookie.Delete(HttpContext);
            return Ok(new { message = "Đã đăng xuất." });
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDTO dto)
        {
            if (dto is null)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                return Unauthorized(new { message = "Không xác định được người dùng." });

            var (success, error, notFound) = await _authService.ChangePasswordAsync(userId, dto);
            if (notFound)
                return NotFound(new { message = error });
            if (!success)
                return BadRequest(new { message = error });

            return Ok(new { message = "Đổi mật khẩu thành công." });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDTO dto)
        {
            if (dto is null)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            var (success, error) = await _authService.ForgotPasswordAsync(dto);
            if (!success)
                return BadRequest(new { message = error });

            return Ok(new { message = "Nếu tài khoản tồn tại, mã OTP đặt lại mật khẩu đã được gửi qua email." });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDTO dto)
        {
            if (dto is null)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            var (success, error) = await _authService.ResetPasswordAsync(dto);
            if (!success)
                return BadRequest(new { message = error });

            return Ok(new { message = "Đặt lại mật khẩu thành công." });
        }

        [Authorize]
        [HttpGet("ping")]
        public IActionResult Ping() => Ok(new { message = "Session is valid." });
    }
}
