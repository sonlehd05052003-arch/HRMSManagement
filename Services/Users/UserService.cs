using AutoMapper;
using HRManagement.DataAcess.Interfaces;
using HRManagement.DTOs;
using HRManagement.Models;
using HRManagement.Services.Emails;

namespace HRManagement.Services.Users
{
    public class UserService : IUserService
    {
        private const string AdminRoleName = "ADMIN";

        private readonly IUserRepository _userRepository;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IUserAccountValidationService _validationService;
        private readonly IEmailService _emailService;
        private readonly IMapper _mapper;

        public UserService(
            IUserRepository userRepository,
            IEmployeeRepository employeeRepository,
            IUserAccountValidationService validationService,
            IEmailService emailService,
            IMapper mapper)
        {
            _userRepository = userRepository;
            _employeeRepository = employeeRepository;
            _validationService = validationService;
            _emailService = emailService;
            _mapper = mapper;
        }

        public async Task<IEnumerable<UserEmployeeOptionDTO>> GetEmployeeOptionsAsync()
        {
            var employees = await _employeeRepository.GetAllEmployeesAsync();
            return employees
                .Select(e => new UserEmployeeOptionDTO
                {
                    EmployeeId = e.EmployeeId,
                    EmployeeCode = e.EmployeeCode,
                    FullName = e.FullName,
                    Email = e.Email
                })
                .ToList();
        }

        public async Task<IEnumerable<UserResponseDTO>> GetAllUsersAsync()
        {
            var users = await _userRepository.GetAllWithRolesAsync();
            return _mapper.Map<IEnumerable<UserResponseDTO>>(users);
        }

        public async Task<UserResponseDTO?> GetUserAsync(int id)
        {
            var user = await _userRepository.GetByIdWithRolesAsync(id);
            return user is null ? null : _mapper.Map<UserResponseDTO>(user);
        }

        public async Task<(bool Success, string? Error, string? Username)> CreateUserAsync(CreateUserDTO dto)
        {
            if (dto.EmployeeId <= 0)
                return (false, "EmployeeId is required", null);

            if (string.IsNullOrWhiteSpace(dto.Email))
                return (false, "Email is required", null);

            var employee = await _employeeRepository.GetEmployeeByIdAsync(dto.EmployeeId);
            if (employee is null)
                return (false, "Employee does not exist", null);

            var roleValidation = await _validationService.ValidateRoleSelectionAsync(new List<int> { dto.RoleId });
            if (!roleValidation.IsValid)
                return (false, roleValidation.ErrorMessage, null);

            var approvalError = await _validationService.ValidateApprovalRouteAsync(employee, roleValidation.RoleNames);
            if (approvalError is not null)
                return (false, approvalError, null);

            if (await _userRepository.ExistsByEmployeeIdAsync(dto.EmployeeId))
                return (false, "This employee already has an account", null);

            var email = dto.Email.Trim().ToLowerInvariant();
            if (await _userRepository.ExistsByEmailAsync(email))
                return (false, "Email already exists", null);

            var username = await GenerateUniqueUsernameAsync(employee.FirstName, employee.LastName, employee.EmployeeId);
            var tempPassword = Guid.NewGuid().ToString("N")[..8];

            var user = new User
            {
                Username = username,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword),
                EmployeeId = dto.EmployeeId,
                IsActive = true,
                CreatedDate = DateTime.UtcNow
            };

            await _userRepository.AddAsync(user);

            if (roleValidation.ValidRoles.Any())
            {
                var userRoles = roleValidation.ValidRoles
                    .Select(r => new UserRole { UserId = user.UserId, RoleId = r.RoleId });
                await _userRepository.AddUserRolesAsync(userRoles);
            }

            await _emailService.SendAsync(
                email,
                "Thong tin tai khoan HR System",
                BuildCreateAccountEmail(employee.LastName, employee.FirstName, username, tempPassword)
            );

            return (true, null, username);
        }

        public async Task<(bool Success, string? Error)> UpdateUserAsync(int id, UpdateUserDTO dto)
        {
            if (dto.EmployeeId <= 0)
                return (false, "EmployeeId is required");

            var user = await _userRepository.GetByIdWithRolesAsync(id);
            if (user is null)
                return (false, null); // not found

            var employee = await _employeeRepository.GetEmployeeByIdAsync(dto.EmployeeId);
            if (employee is null)
                return (false, "Employee does not exist");

            if (await _userRepository.ExistsByEmployeeIdAsync(dto.EmployeeId, excludeUserId: id))
                return (false, "This employee is already linked to another account");

            var roleValidation = await _validationService.ValidateRoleSelectionAsync(new List<int> { dto.RoleId });
            if (!roleValidation.IsValid)
                return (false, roleValidation.ErrorMessage);

            var approvalError = await _validationService.ValidateApprovalRouteAsync(employee, roleValidation.RoleNames);
            if (approvalError is not null)
                return (false, approvalError);

            var normalizedEmail = dto.Email.Trim().ToLowerInvariant();
            if (await _userRepository.ExistsByEmailAsync(normalizedEmail, excludeUserId: id))
                return (false, "Email already exists");

            var oldEmail = user.Email;
            var oldIsActive = user.IsActive;
            var oldRoles = user.UserRoles.Select(x => x.RoleId).OrderBy(x => x).ToList();

            user.Email = normalizedEmail;
            user.IsActive = dto.IsActive;
            user.EmployeeId = dto.EmployeeId;
            user.ModifiedDate = DateTime.UtcNow;

            var newUserRoles = roleValidation.ValidRoles
                .Select(r => new UserRole { UserId = id, RoleId = r.RoleId });

            await _userRepository.ReplaceUserRolesAsync(user.UserRoles, newUserRoles);
            await _userRepository.SaveChangesAsync();

            var newRoles = roleValidation.ValidRoles.Select(r => r.RoleId).OrderBy(x => x).ToList();
            var emailChanged = !string.Equals(oldEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase);
            var statusChanged = oldIsActive != dto.IsActive;
            var roleChanged = !oldRoles.SequenceEqual(newRoles);

            if (emailChanged || statusChanged || roleChanged)
            {
                await _emailService.SendAsync(
                    normalizedEmail,
                    "Cap nhat thong tin tai khoan HR System",
                    BuildUpdateAccountEmail(normalizedEmail, dto.IsActive, emailChanged, statusChanged, roleChanged)
                );
            }

            return (true, null);
        }

        public async Task<(bool Success, string? Error)> DeactivateUserAsync(int id)
        {
            var user = await _userRepository.FindAsync(id);
            if (user is null)
                return (false, null);

            user.IsActive = false;
            user.ModifiedDate = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();

            await _emailService.SendAsync(
                user.Email,
                "Tai khoan HR System da bi ngung hoat dong",
                BuildDeactivateEmail()
            );

            return (true, null);
        }

        public async Task<(bool Success, string? Error)> ActivateUserAsync(int id)
        {
            var user = await _userRepository.GetByIdWithRolesAsync(id);
            if (user is null)
                return (false, null);

            if (user.EmployeeId is null)
                return (false, "This account is not linked to an employee.");

            var employee = await _employeeRepository.GetEmployeeByIdAsync(user.EmployeeId.Value);
            if (employee is null)
                return (false, "Employee does not exist");

            var approvalError = await _validationService.ValidateApprovalRouteAsync(
                employee,
                user.UserRoles.Select(ur => ur.Role.RoleName));
            if (approvalError is not null)
                return (false, approvalError);

            user.IsActive = true;
            user.ModifiedDate = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();

            await _emailService.SendAsync(
                user.Email,
                "Tai khoan HR System da duoc kich hoat",
                BuildActivateEmail()
            );

            return (true, null);
        }

        /// <summary>
        /// Đặt mật khẩu mới do quản trị viên nhập cho tài khoản, đăng xuất mọi phiên đang mở và báo cho người dùng qua email.
        /// Tài khoản ADMIN chỉ được đặt lại bởi ADMIN / SuperAdmin, để người chỉ có quyền User.Manage không chiếm được tài khoản quản trị.
        /// </summary>
        public async Task<(bool Success, string? Error)> ResetPasswordAsync(int id, ResetUserPasswordDTO dto, int currentUserId, bool canResetAdmins)
        {
            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                return (false, "Mật khẩu mới là bắt buộc.");

            if (dto.NewPassword != dto.ConfirmNewPassword)
                return (false, "Mật khẩu mới và xác nhận mật khẩu không khớp.");

            if (dto.NewPassword.Length < 6)
                return (false, "Mật khẩu mới phải có ít nhất 6 ký tự.");

            var user = await _userRepository.GetByIdWithRolesAsync(id);
            if (user is null)
                return (false, null);

            if (user.UserId == currentUserId)
                return (false, "Dùng chức năng Đổi mật khẩu để đổi mật khẩu của chính bạn.");

            if (!user.IsActive)
                return (false, "Tài khoản đang bị vô hiệu hóa, không thể đặt lại mật khẩu.");

            if (!canResetAdmins && user.UserRoles.Any(ur => ur.Role.RoleName == AdminRoleName))
                return (false, "Chỉ quản trị viên mới được đặt lại mật khẩu tài khoản ADMIN.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            user.PasswordResetOtp = null;
            user.PasswordResetOtpExpiry = null;
            user.ModifiedDate = DateTime.UtcNow;

            // Tokens carry the LastLogin they were issued with; moving it makes every open session fail validation.
            var now = DateTime.UtcNow;
            user.LastLogin = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second);

            await _userRepository.SaveChangesAsync();

            await _emailService.SendAsync(
                user.Email,
                "Mật khẩu tài khoản HR System đã được đặt lại",
                BuildResetPasswordEmail(user.Username));

            return (true, null);
        }

        private async Task<string> GenerateUniqueUsernameAsync(string firstName, string lastName, int employeeId)
        {
            var baseUsername = _validationService.GenerateBaseUsername(firstName, lastName);
            if (string.IsNullOrWhiteSpace(baseUsername))
                baseUsername = $"user{employeeId}";

            var username = baseUsername;
            var suffix = 1;
            while (await _userRepository.ExistsByUsernameAsync(username))
            {
                username = $"{baseUsername}{suffix}";
                suffix++;
            }
            return username;
        }

        private static string BuildCreateAccountEmail(string lastName, string firstName, string username, string tempPassword) =>
            $@"<h3>Tai khoan HR System</h3>
            <p>Xin chao {lastName} {firstName},</p>
            <p>Tai khoan cua ban da duoc tao thanh cong:</p>
            <ul>
                <li><b>Username:</b> {username}</li>
                <li><b>Password tam:</b> {tempPassword}</li>
            </ul>
            <p>Vui long dang nhap va doi mat khau ngay.</p>";

        private static string BuildUpdateAccountEmail(string email, bool isActive, bool emailChanged, bool statusChanged, bool roleChanged) =>
            $@"<h3>Cap nhat tai khoan HR System</h3>
            <p>Xin chao,</p>
            <p>Thong tin tai khoan cua ban vua duoc quan tri vien cap nhat:</p>
            <ul>
                {(emailChanged ? $"<li><b>Email moi:</b> {email}</li>" : "")}
                {(statusChanged ? $"<li><b>Trang thai:</b> {(isActive ? "Kich hoat" : "Ngung hoat dong")}</li>" : "")}
                {(roleChanged ? "<li><b>Vai tro:</b> Da duoc cap nhat</li>" : "")}
            </ul>
            <p>Neu ban khong nhan ra thay doi nay, vui long lien he quan tri he thong.</p>";

        // The new password is not sent: the administrator who set it hands it over
        private static string BuildResetPasswordEmail(string username) =>
            $@"<h3>Mật khẩu của bạn đã được đặt lại</h3>
            <p>Quản trị viên vừa đặt lại mật khẩu tài khoản HR System <b>{username}</b> của bạn.</p>
            <p>Mọi phiên đăng nhập cũ đã bị đăng xuất. Hãy liên hệ quản trị viên để nhận mật khẩu mới, đăng nhập và đổi lại mật khẩu của riêng bạn.</p>
            <p>Nếu bạn không yêu cầu việc này, hãy liên hệ quản trị viên ngay.</p>";

        private static string BuildDeactivateEmail() =>
            $@"<h3>Thong bao trang thai tai khoan HR System</h3>
            <p>Xin chao,</p>
            <p>Tai khoan cua ban da bi <b>ngung hoat dong</b> boi quan tri vien he thong.</p>
            <ul>
                <li><b>Thoi gian:</b> {DateTime.UtcNow:dd/MM/yyyy HH:mm}</li>
            </ul>
            <p>Neu ban cho rang day la nham lan, vui long lien he bo phan quan tri.</p>";

        private static string BuildActivateEmail() =>
            $@"<h3>Thong bao trang thai tai khoan HR System</h3>
            <p>Xin chao,</p>
            <p>Tai khoan cua ban da duoc <b>kich hoat lai</b> va co the dang nhap he thong.</p>
            <ul>
                <li><b>Thoi gian:</b> {DateTime.UtcNow:dd/MM/yyyy HH:mm}</li>
            </ul>
            <p>Vui long dang nhap de tiep tuc su dung he thong.</p>";
    }
}
