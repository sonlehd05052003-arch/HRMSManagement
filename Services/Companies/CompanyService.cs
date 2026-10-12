using System.Security.Cryptography;
using HRManagement.DTOs;
using HRManagement.Models;
using HRManagement.Services.Emails;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Services.Companies
{
    public interface ICompanyService
    {
        Task<List<CompanyListItemDTO>> GetAllAsync();
        Task<CompanyListItemDTO?> GetByIdAsync(int companyId);
        Task<(CreateCompanyResultDTO? Result, string? Error)> CreateAsync(CreateCompanyDTO dto);
        Task<(bool Found, string? Error)> UpdateAsync(int companyId, CompanyWriteDTO dto);
        Task<(bool Found, string? Error)> SetStatusAsync(int companyId, bool isActive);
        Task<(ResetAdminPasswordResultDTO? Result, string? Error)> ResetAdminPasswordAsync(int companyId, int userId);
    }

    /// <summary>
    /// Quản lý công ty cho SuperAdmin. SuperAdmin không thuộc công ty nào nên mọi truy vấn ở đây
    /// đều bỏ global filter và chỉ định CompanyId tường minh.
    /// </summary>
    public class CompanyService : ICompanyService
    {
        /// <summary>Công ty có dữ liệu mẫu (role, loại nghỉ phép, ca, cấu hình lương) được chép sang công ty mới.</summary>
        private const int TemplateCompanyId = Company.DefaultCompanyId;

        private const string AdminRoleName = "ADMIN";

        /// <summary>Cấu hình được chép sang công ty mới; vị trí văn phòng, người duyệt dự phòng… là riêng từng công ty nên bỏ qua.</summary>
        private static readonly string[] CopiedSettingPrefixes = { "Payroll.", "ResignationNoticeDays" };

        private readonly HrmsDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<CompanyService> _logger;

        public CompanyService(HrmsDbContext context, IEmailService emailService, ILogger<CompanyService> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task<List<CompanyListItemDTO>> GetAllAsync()
        {
            return await ProjectToListItems(_context.Companies.AsNoTracking())
                .OrderBy(c => c.CompanyId)
                .ToListAsync();
        }

        public async Task<CompanyListItemDTO?> GetByIdAsync(int companyId)
        {
            return await ProjectToListItems(_context.Companies.AsNoTracking().Where(c => c.CompanyId == companyId))
                .FirstOrDefaultAsync();
        }

        private IQueryable<CompanyListItemDTO> ProjectToListItems(IQueryable<Company> companies) =>
            companies.Select(c => new CompanyListItemDTO
            {
                CompanyId = c.CompanyId,
                CompanyCode = c.CompanyCode,
                CompanyName = c.CompanyName,
                TaxCode = c.TaxCode,
                Email = c.Email,
                Phone = c.Phone,
                Address = c.Address,
                IsActive = c.IsActive,
                CreatedDate = c.CreatedDate,
                EmployeeCount = _context.Employees.IgnoreQueryFilters().Count(e => e.CompanyId == c.CompanyId),
                UserCount = _context.Users.IgnoreQueryFilters().Count(u => u.CompanyId == c.CompanyId),
                Admins = _context.UserRoles.IgnoreQueryFilters()
                    .Where(ur => ur.User.CompanyId == c.CompanyId && ur.Role.RoleName == AdminRoleName)
                    .OrderBy(ur => ur.UserId)
                    .Select(ur => new CompanyAdminDTO
                    {
                        UserId = ur.UserId,
                        Username = ur.User.Username,
                        Email = ur.User.Email,
                        IsActive = ur.User.IsActive
                    })
                    .ToList()
            });

        public async Task<(CreateCompanyResultDTO? Result, string? Error)> CreateAsync(CreateCompanyDTO dto)
        {
            var code = dto.CompanyCode.Trim().ToUpperInvariant();
            var username = dto.Admin.Username.Trim();
            var adminEmail = dto.Admin.Email.Trim();

            if (await _context.Companies.AnyAsync(c => c.CompanyCode == code))
                return (null, $"Mã công ty '{code}' đã tồn tại.");

            // Username và email tài khoản là duy nhất toàn hệ thống
            if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Username == username))
                return (null, $"Username '{username}' đã được sử dụng.");
            if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == adminEmail))
                return (null, $"Email '{adminEmail}' đã được dùng cho tài khoản khác.");

            var templateRoles = await _context.Roles.IgnoreQueryFilters()
                .AsNoTracking()
                .Include(r => r.RolePermissions)
                .Include(r => r.RoleMenus)
                .Where(r => r.CompanyId == TemplateCompanyId)
                .ToListAsync();

            if (!templateRoles.Any(r => r.RoleName == AdminRoleName))
                return (null, $"Công ty mẫu chưa có role {AdminRoleName}, không thể khởi tạo công ty mới.");

            var tempPassword = GenerateTempPassword();

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var company = new Company
            {
                CompanyCode = code,
                CompanyName = dto.CompanyName.Trim(),
                TaxCode = dto.TaxCode?.Trim(),
                Email = dto.Email?.Trim(),
                Phone = dto.Phone?.Trim(),
                Address = dto.Address?.Trim(),
                IsActive = true,
                CreatedDate = AppTime.Now
            };
            _context.Companies.Add(company);
            await _context.SaveChangesAsync();

            var companyId = company.CompanyId;

            // Role mẫu kèm quyền và menu
            var roles = templateRoles.Select(t => new Role
            {
                CompanyId = companyId,
                RoleName = t.RoleName,
                Description = t.Description,
                IsActive = t.IsActive,
                CreatedDate = AppTime.Now,
                RolePermissions = t.RolePermissions.Select(rp => new RolePermission { PermissionId = rp.PermissionId }).ToList(),
                RoleMenus = t.RoleMenus.Select(rm => new RoleMenu { MenuId = rm.MenuId }).ToList()
            }).ToList();
            _context.Roles.AddRange(roles);

            await CopyMasterDataAsync(companyId);

            // Tài khoản admin đầu tiên, kèm hồ sơ nhân viên để dùng được các chức năng cá nhân (chấm công, nghỉ phép…)
            var adminEmployee = new Employee
            {
                CompanyId = companyId,
                EmployeeCode = "EMP0001",
                FirstName = dto.Admin.FirstName.Trim(),
                LastName = dto.Admin.LastName.Trim(),
                Email = adminEmail,
                JoinDate = DateOnly.FromDateTime(AppTime.Today),
                EmploymentStatus = "Active"
            };
            _context.Employees.Add(adminEmployee);

            var adminUser = new User
            {
                CompanyId = companyId,
                Username = username,
                Email = adminEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword),
                Employee = adminEmployee,
                IsActive = true,
                CreatedDate = DateTime.UtcNow
            };
            adminUser.UserRoles.Add(new UserRole { Role = roles.First(r => r.RoleName == AdminRoleName) });
            _context.Users.Add(adminUser);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            await _emailService.SendAsync(
                adminEmail,
                "Tài khoản quản trị HR System của công ty bạn đã được tạo",
                BuildAdminWelcomeEmail(company.CompanyName, dto.Admin.LastName, dto.Admin.FirstName, username, tempPassword));

            _logger.LogInformation("Created company {CompanyId} ({CompanyCode}) with admin {Username}.", companyId, code, username);

            return (new CreateCompanyResultDTO
            {
                CompanyId = companyId,
                AdminUsername = username,
                TemporaryPassword = tempPassword
            }, null);
        }

        private async System.Threading.Tasks.Task CopyMasterDataAsync(int companyId)
        {
            var leaveTypes = await _context.LeaveTypes.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == TemplateCompanyId).ToListAsync();
            foreach (var x in leaveTypes) { x.LeaveTypeId = 0; x.CompanyId = companyId; x.CreatedBy = null; x.CreatedDate = AppTime.Now; }
            _context.LeaveTypes.AddRange(leaveTypes);

            var shifts = await _context.Shifts.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == TemplateCompanyId).ToListAsync();
            foreach (var x in shifts) { x.ShiftId = 0; x.CompanyId = companyId; x.CreatedBy = null; x.CreatedDate = AppTime.Now; }
            _context.Shifts.AddRange(shifts);

            var policies = await _context.PayrollPolicies.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == TemplateCompanyId).ToListAsync();
            foreach (var x in policies) { x.PolicyId = 0; x.CompanyId = companyId; x.CreatedBy = null; x.ModifiedBy = null; x.ModifiedDate = null; x.CreatedDate = AppTime.Now; }
            _context.PayrollPolicies.AddRange(policies);

            var settings = (await _context.SystemSettings.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.CompanyId == TemplateCompanyId).ToListAsync())
                .Where(x => CopiedSettingPrefixes.Any(p => x.SettingKey.StartsWith(p, StringComparison.Ordinal)))
                .ToList();
            foreach (var x in settings) { x.SettingId = 0; x.CompanyId = companyId; x.ModifiedBy = null; x.ModifiedDate = AppTime.Now; }
            _context.SystemSettings.AddRange(settings);
        }

        public async Task<(bool Found, string? Error)> UpdateAsync(int companyId, CompanyWriteDTO dto)
        {
            var company = await _context.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId);
            if (company is null)
                return (false, null);

            var code = dto.CompanyCode.Trim().ToUpperInvariant();
            if (code != company.CompanyCode && await _context.Companies.AnyAsync(c => c.CompanyCode == code))
                return (true, $"Mã công ty '{code}' đã tồn tại.");

            company.CompanyCode = code;
            company.CompanyName = dto.CompanyName.Trim();
            company.TaxCode = dto.TaxCode?.Trim();
            company.Email = dto.Email?.Trim();
            company.Phone = dto.Phone?.Trim();
            company.Address = dto.Address?.Trim();
            company.ModifiedDate = AppTime.Now;

            await _context.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool Found, string? Error)> SetStatusAsync(int companyId, bool isActive)
        {
            var company = await _context.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId);
            if (company is null)
                return (false, null);

            company.IsActive = isActive;
            company.ModifiedDate = AppTime.Now;
            await _context.SaveChangesAsync();
            return (true, null);
        }

        /// <summary>
        /// Cấp mật khẩu tạm mới cho một tài khoản ADMIN của công ty và đăng xuất mọi phiên đang mở của tài khoản đó.
        /// Chỉ áp dụng cho admin: tài khoản khác trong công ty do admin công ty tự quản lý.
        /// </summary>
        public async Task<(ResetAdminPasswordResultDTO? Result, string? Error)> ResetAdminPasswordAsync(int companyId, int userId)
        {
            var user = await _context.Users.IgnoreQueryFilters()
                .Include(u => u.Company)
                .FirstOrDefaultAsync(u => u.UserId == userId
                    && u.CompanyId == companyId
                    && u.UserRoles.Any(ur => ur.Role.RoleName == AdminRoleName));

            if (user is null)
                return (null, null);

            if (!user.IsActive)
                return (null, "Tài khoản admin đang bị vô hiệu hóa, không thể đặt lại mật khẩu.");

            var tempPassword = GenerateTempPassword();
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);
            user.PasswordResetOtp = null;
            user.PasswordResetOtpExpiry = null;
            user.ModifiedDate = DateTime.UtcNow;

            // Tokens carry the LastLogin they were issued with; moving it makes every open session fail validation.
            var now = DateTime.UtcNow;
            user.LastLogin = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second);

            await _context.SaveChangesAsync();

            await _emailService.SendAsync(
                user.Email,
                "Mật khẩu tài khoản quản trị HR System đã được đặt lại",
                BuildPasswordResetEmail(user.Company!.CompanyName, user.Username, tempPassword));

            _logger.LogInformation("SuperAdmin reset password of admin {Username} (company {CompanyId}).", user.Username, companyId);

            return (new ResetAdminPasswordResultDTO { Username = user.Username, TemporaryPassword = tempPassword }, null);
        }

        private static string GenerateTempPassword() =>
            Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();

        private static string BuildPasswordResetEmail(string companyName, string username, string tempPassword) =>
            $@"<h3>Mật khẩu của bạn đã được đặt lại</h3>
            <p>Quản trị viên hệ thống vừa đặt lại mật khẩu tài khoản quản trị của <b>{companyName}</b>:</p>
            <ul>
                <li><b>Username:</b> {username}</li>
                <li><b>Mật khẩu tạm:</b> {tempPassword}</li>
            </ul>
            <p>Mọi phiên đăng nhập cũ đã bị đăng xuất. <b>Vui lòng đăng nhập và đổi mật khẩu ngay.</b></p>
            <p>Nếu bạn không yêu cầu việc này, hãy liên hệ quản trị viên hệ thống.</p>";

        private static string BuildAdminWelcomeEmail(string companyName, string lastName, string firstName, string username, string tempPassword) =>
            $@"<h3>Chào mừng {companyName} đến với HR System</h3>
            <p>Xin chào {lastName} {firstName},</p>
            <p>Công ty của bạn đã được khởi tạo trên hệ thống. Tài khoản quản trị (ADMIN) của bạn:</p>
            <ul>
                <li><b>Username:</b> {username}</li>
                <li><b>Mật khẩu tạm:</b> {tempPassword}</li>
            </ul>
            <p><b>Vui lòng đăng nhập và đổi mật khẩu ngay.</b> Sau đó bạn có thể tạo phòng ban, chức vụ, nhân viên và tài khoản cho công ty.</p>";
    }
}
