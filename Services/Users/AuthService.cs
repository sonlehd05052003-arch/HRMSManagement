using HRManagement.Authorization;
using HRManagement.DataAcess.Interfaces;
using HRManagement.DTOs;
using HRManagement.DTOs.Auths;
using HRManagement.Services.Emails;
using HRManagement.Services.Tenants;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace HRManagement.Services.Users
{
    public class AuthService : IAuthService
    {
        private readonly IAuthRepository _authRepository;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;

        public AuthService(
            IAuthRepository authRepository,
            IEmailService emailService,
            IConfiguration configuration,
            IMemoryCache cache)
        {
            _authRepository = authRepository;
            _emailService = emailService;
            _configuration = configuration;
            _cache = cache;
        }

        public async Task<(bool Success, string? Error, string? Token)> LoginAsync(LoginRequestDTO dto)
        {
            var user = await _authRepository.GetUserForLoginAsync(dto.Username);

            if (user is null)
                return (false, "Username không tồn tại", null);

            if (!user.IsActive)
                return (false, "Tài khoản đã bị vô hiệu hóa", null);

            if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                return (false, "Mật khẩu không đúng", null);

            if (user.Company is { IsActive: false })
                return (false, "Công ty của bạn đã bị tạm khóa trên hệ thống", null);

            var now = DateTime.UtcNow;
            user.LastLogin = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second);
            await _authRepository.SaveChangesAsync();

            var lastLoginStr = user.LastLogin.Value.ToString("yyyyMMddHHmmss");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(ClaimTypes.Name, user.Username),
                new("EmployeeID", user.EmployeeId?.ToString() ?? ""),
                new("IsTopLevel", (user.Employee?.Position?.IsTopLevel ?? false).ToString().ToLower()),
                new("PositionName", user.Employee?.Position?.PositionName ?? ""),
                new("LastLogin", lastLoginStr),
                new(TenantContext.ClaimType, user.CompanyId?.ToString() ?? "")
            };

            if (user.EmployeeId.HasValue)
                claims.Add(new Claim("employeeId", user.EmployeeId.Value.ToString()));

            foreach (var userRole in user.UserRoles)
                claims.Add(new Claim(ClaimTypes.Role, userRole.Role.RoleName));

            // Tài khoản không thuộc công ty nào là tài khoản hệ thống
            if (user.CompanyId == null)
                claims.Add(new Claim(ClaimTypes.Role, PlatformRoles.SuperAdmin));

            var token = GenerateJwtToken(claims);
            return (true, null, token);
        }

        public async Task<(bool Success, string? Error, bool NotFound)> ChangePasswordAsync(int userId, ChangePasswordDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
                return (false, "Mật khẩu hiện tại là bắt buộc.", false);

            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                return (false, "Mật khẩu mới là bắt buộc.", false);

            if (string.IsNullOrWhiteSpace(dto.ConfirmNewPassword))
                return (false, "Xác nhận mật khẩu mới là bắt buộc.", false);

            if (dto.NewPassword != dto.ConfirmNewPassword)
                return (false, "Mật khẩu mới và xác nhận mật khẩu không khớp.", false);

            if (dto.NewPassword.Length < 6)
                return (false, "Mật khẩu mới phải có ít nhất 6 ký tự.", false);

            var user = await _authRepository.GetUserByIdAsync(userId);
            if (user is null)
                return (false, "Người dùng không tồn tại.", true);

            if (!user.IsActive)
                return (false, "Tài khoản đã bị vô hiệu hóa.", false);

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                return (false, "Mật khẩu hiện tại không đúng.", false);

            if (BCrypt.Net.BCrypt.Verify(dto.NewPassword, user.PasswordHash))
                return (false, "Mật khẩu mới không được trùng với mật khẩu hiện tại.", false);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _authRepository.SaveChangesAsync();

            return (true, null, false);
        }

        public async Task<(bool Success, string? Error)> ForgotPasswordAsync(ForgotPasswordRequestDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.EmailOrUsername))
                return (false, "Email hoặc username là bắt buộc.");

            var user = await _authRepository.GetUserByEmailOrUsernameAsync(dto.EmailOrUsername);

            if (user is null)
                return (false, "Email hoặc Username không tồn tại trong hệ thống.");

            if (!user.IsActive)
                return (false, "Tài khoản của bạn hiện đang bị vô hiệu hóa.");

            var otp = GenerateOtp();
            user.PasswordResetOtp = otp;
            user.PasswordResetOtpExpiry = DateTime.UtcNow.AddMinutes(5);
            await _authRepository.SaveChangesAsync();
            _cache.Remove(OtpAttemptsKey(user.UserId));

            await _emailService.SendAsync(
                user.Email,
                "Mã OTP đặt lại mật khẩu HR System",
                BuildOtpEmail(user.Username, otp)
            );

            return (true, null);
        }

        public async Task<(bool Success, string? Error)> ResetPasswordAsync(ResetPasswordDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.EmailOrUsername))
                return (false, "Email hoặc username là bắt buộc.");

            if (string.IsNullOrWhiteSpace(dto.Otp))
                return (false, "OTP là bắt buộc.");

            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                return (false, "Mật khẩu mới là bắt buộc.");

            if (string.IsNullOrWhiteSpace(dto.ConfirmNewPassword))
                return (false, "Xác nhận mật khẩu mới là bắt buộc.");

            if (dto.NewPassword != dto.ConfirmNewPassword)
                return (false, "Mật khẩu mới và xác nhận mật khẩu không khớp.");

            if (dto.NewPassword.Length < 6)
                return (false, "Mật khẩu mới phải có ít nhất 6 ký tự.");

            var user = await _authRepository.GetUserByEmailOrUsernameAsync(dto.EmailOrUsername);
            if (user is null)
                return (false, "Thông tin đặt lại mật khẩu không hợp lệ.");

            if (!user.IsActive)
                return (false, "Tài khoản đã bị vô hiệu hóa.");

            if (string.IsNullOrWhiteSpace(user.PasswordResetOtp) || user.PasswordResetOtpExpiry is null)
                return (false, "OTP không hợp lệ hoặc chưa được tạo.");

            if (user.PasswordResetOtpExpiry < DateTime.UtcNow)
                return (false, "OTP đã hết hạn.");

            if (!OtpMatches(user.PasswordResetOtp, dto.Otp.Trim()))
            {
                // A 6-digit code falls to guessing without a cap, even with the per-IP rate limit
                var attemptsKey = OtpAttemptsKey(user.UserId);
                var attempts = (_cache.Get<int?>(attemptsKey) ?? 0) + 1;
                if (attempts >= MaxOtpAttempts)
                {
                    user.PasswordResetOtp = null;
                    user.PasswordResetOtpExpiry = null;
                    await _authRepository.SaveChangesAsync();
                    _cache.Remove(attemptsKey);
                    return (false, "Bạn đã nhập sai OTP quá nhiều lần. Vui lòng yêu cầu mã OTP mới.");
                }

                _cache.Set(attemptsKey, attempts, DateTime.SpecifyKind(user.PasswordResetOtpExpiry.Value, DateTimeKind.Utc));
                return (false, "OTP không đúng.");
            }

            if (BCrypt.Net.BCrypt.Verify(dto.NewPassword, user.PasswordHash))
                return (false, "Mật khẩu mới không được trùng với mật khẩu hiện tại.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            user.PasswordResetOtp = null;
            user.PasswordResetOtpExpiry = null;
            await _authRepository.SaveChangesAsync();
            _cache.Remove(OtpAttemptsKey(user.UserId));

            return (true, null);
        }

        private string GenerateJwtToken(IEnumerable<Claim> claims)
        {
            var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(30),
                Issuer = _configuration["Jwt:Issuer"],
                Audience = _configuration["Jwt:Audience"],
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };
            var handler = new JwtSecurityTokenHandler();
            return handler.WriteToken(handler.CreateToken(tokenDescriptor));
        }

        private const int MaxOtpAttempts = 5;

        private static string OtpAttemptsKey(int userId) => $"otp-attempts:{userId}";

        private static string GenerateOtp() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        private static bool OtpMatches(string expected, string actual) =>
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

        private static string BuildOtpEmail(string username, string otp) =>
            $@"<h3>Đặt lại mật khẩu HR System</h3>
            <p>Xin chào {username},</p>
            <p>Mã OTP để đặt lại mật khẩu của bạn là:</p>
            <h2>{otp}</h2>
            <p>Mã này có hiệu lực trong <b>5 phút</b>.</p>
            <p>Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này.</p>";
    }
}
