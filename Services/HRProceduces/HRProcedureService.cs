using HRManagement.DataAcess.Interfaces;
using HRManagement.DTOs;
using HRManagement.Models;
using HRManagement.Services.CurrentUsers;
using HRManagement.Services.Emails;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Services.HRProceduces
{
    public class HRProcedureService : IHRProcedureService
    {
        private readonly IHRProcedureRepository _hrProcedureRepository;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly HrmsDbContext _context;
        private readonly Approvals.ITopLevelResolver _topLevelResolver;
        private readonly IEmailService _emailService;

        public HRProcedureService(
            IHRProcedureRepository hrProcedureRepository,
            IEmployeeRepository employeeRepository,
            ICurrentUserService currentUserService,
            HrmsDbContext context,
            Approvals.ITopLevelResolver topLevelResolver,
            IEmailService emailService)
        {
            _hrProcedureRepository = hrProcedureRepository;
            _employeeRepository = employeeRepository;
            _currentUserService = currentUserService;
            _context = context;
            _topLevelResolver = topLevelResolver;
            _emailService = emailService;
        }

        // ─────────────────────────────────────────
        // APPROVE  (Phase 2: tách Approve khỏi Apply)
        // ─────────────────────────────────────────
        public async Task<HRProcedureResponseDto> ApproveProcedureAsync(int procedureId, ApproveHRProcedureDto approveDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var procedure = await _hrProcedureRepository.GetByIdWithDetailsAsync(procedureId)
                    ?? throw new KeyNotFoundException("HR procedure not found.");

                if (procedure.Status != "Pending")
                    throw new InvalidOperationException("Cannot approve: only Pending procedures can be approved.");

                // ── Mới: Thắt chặt quyền phê duyệt ──────────────────
                var isAdmin = _currentUserService.RoleName == "ADMIN";
                bool isAuthorized = isAdmin;

                if (!isAuthorized)
                {
                    try
                    {
                        var currentEmpId = await _currentUserService.GetCurrentEmployeeIdAsync();
                        isAuthorized = await _topLevelResolver.IsTopLevelEmployeeAsync(currentEmpId);
                    }
                    catch { /* Không phải nhân viên hoặc không tìm thấy profile */ }
                }

                if (!isAuthorized)
                {
                    throw new UnauthorizedAccessException("Only ADMIN or TOP-LEVEL employees can approve HR procedures.");
                }
                // ──────────────────────────────────────────────────
                
                var finalApproverId = isAdmin ? (int?)null : await _currentUserService.GetCurrentEmployeeIdAsync();
                procedure.Status = "Approved";
                procedure.ApprovedDate = DateTime.UtcNow;
                procedure.ApprovedBy = finalApproverId;
                procedure.ReviewedDate = DateTime.UtcNow;
                procedure.ReviewedBy = finalApproverId;

                // Phase 2: chỉ apply ngay nếu EffectiveDate <= hôm nay
                if (ShouldApplyNow(procedure))
                {
                    await ApplyProcedureToEmployeeAsync(procedure, finalApproverId);
                    procedure.AppliedDate = DateTime.UtcNow;
                    procedure.AppliedBy = finalApproverId;
                }

                await _hrProcedureRepository.UpdateAsync(procedure);
                await transaction.CommitAsync();

                return await BuildResponseDtoAsync(procedure);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ─────────────────────────────────────────
        // APPLY riêng (Phase 2: dùng cho scheduled job hoặc manual trigger)
        // ─────────────────────────────────────────
        public async Task<HRProcedureResponseDto> ApplyApprovedProcedureAsync(int procedureId, int? appliedBy = null)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var procedure = await _hrProcedureRepository.GetByIdWithDetailsAsync(procedureId)
                    ?? throw new KeyNotFoundException("HR procedure not found.");

                if (procedure.Status != "Approved")
                    throw new InvalidOperationException("Cannot apply: procedure must be in Approved status.");

                if (procedure.AppliedDate != null)
                    throw new InvalidOperationException("Procedure has already been applied.");

                // Use provided ID or resolve from current user (if any)
                int? finalAppliedBy = appliedBy;
                if (!finalAppliedBy.HasValue)
                {
                    try { finalAppliedBy = await _currentUserService.GetCurrentEmployeeIdAsync(); } catch { /* Ignore if no HTTP context */ }
                }

                await ApplyProcedureToEmployeeAsync(procedure, finalAppliedBy);
                procedure.AppliedDate = DateTime.UtcNow;
                procedure.AppliedBy = finalAppliedBy;

                await _hrProcedureRepository.UpdateAsync(procedure);
                await transaction.CommitAsync();

                return await BuildResponseDtoAsync(procedure);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ─────────────────────────────────────────
        // SUBMIT  (Phase 1: validation mở rộng)
        // ─────────────────────────────────────────
        public async Task<HRProcedureResponseDto> SubmitProcedureAsync(CreateHRProcedureDto createDto)
        {
            var validTypes = new[] { "Appointment", "Transfer", "Demotion", "Termination" };
            if (!validTypes.Contains(createDto.ProcedureType, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Invalid procedure type. Allowed: Appointment, Transfer, Demotion, Termination.");

            if (!await _hrProcedureRepository.EmployeeExistsAsync(createDto.EmployeeId))
                throw new KeyNotFoundException("Employee not found.");

            // Get employee to check status and join date
            var employee = await _employeeRepository.GetEmployeeByIdAsync(createDto.EmployeeId);
            if (employee == null)
                throw new KeyNotFoundException("Employee not found.");

            // Check employee status
            if (employee.EmploymentStatus != "Active")
                throw new ArgumentException("Employee is not active.");

            // Check join date
            if (employee.JoinDate > createDto.EffectiveDate)
                throw new ArgumentException("Cannot create procedure before join date.");

            if (createDto.EffectiveDate < DateOnly.FromDateTime(AppTime.Today))
                throw new ArgumentException("Effective date cannot be in the past.");

            if (await _hrProcedureRepository.HasActiveProcedureAsync(createDto.EmployeeId, createDto.ProcedureType))
                throw new InvalidOperationException("Employee already has an active procedure of this type.");

            // ── Phase 1: Transfer – cần ít nhất 1 thay đổi ──────────────────
            var type = createDto.ProcedureType;
            if (type.Equals("Transfer", StringComparison.OrdinalIgnoreCase))
            {
                bool hasAnyChange = createDto.NewDepartmentId.HasValue
                    || createDto.NewPositionId.HasValue
                    || createDto.NewManagerId.HasValue
                    || createDto.NewSalary.HasValue;

                if (!hasAnyChange)
                    throw new ArgumentException("Transfer requires at least one of: NewDepartmentId, NewPositionId, NewManagerId, NewSalary.");

                // Validate no-op (so sánh với dữ liệu hiện tại)
                await ValidateNotNoOpAsync(createDto);
            }

            // ── Phase 1: Demotion – bắt buộc NewPositionId ──────────────────
            if (type.Equals("Demotion", StringComparison.OrdinalIgnoreCase) && !createDto.NewPositionId.HasValue)
                throw new ArgumentException("Demotion requires NewPositionId.");

            // ── Validate FK tồn tại ──────────────────────────────────────────
            if (createDto.NewDepartmentId.HasValue && !await _hrProcedureRepository.DepartmentExistsAsync(createDto.NewDepartmentId.Value))
                throw new KeyNotFoundException("NewDepartment not found.");

            if (createDto.NewPositionId.HasValue && !await _hrProcedureRepository.PositionExistsAsync(createDto.NewPositionId.Value))
                throw new KeyNotFoundException("NewPosition not found.");

            // ── Phase 1: Validate NewManager ────────────────────────────────
            if (createDto.NewManagerId.HasValue)
                await ValidateNewManagerAsync(createDto.EmployeeId, createDto.NewManagerId.Value);

            var procedure = new Hrprocedure
            {
                ProcedureNumber  = GenerateProcedureNumber(),
                EmployeeId       = createDto.EmployeeId,
                ProcedureType    = createDto.ProcedureType,
                EffectiveDate    = createDto.EffectiveDate,
                NewDepartmentId  = createDto.NewDepartmentId,
                NewPositionId    = createDto.NewPositionId,
                NewManagerId     = createDto.NewManagerId,
                NewSalary        = createDto.NewSalary,
                Reason           = createDto.Reason,
                Status           = "Pending",
                SubmittedDate    = DateTime.UtcNow,
                SubmittedBy      = await _currentUserService.GetCurrentEmployeeIdAsync()
            };

            await _hrProcedureRepository.AddAsync(procedure);

            return await BuildResponseDtoAsync(procedure);
        }

        // ─────────────────────────────────────────
        // UPDATE
        // ─────────────────────────────────────────
        public async Task<HRProcedureResponseDto> UpdateProcedureAsync(int procedureId, UpdateHRProcedureDto updateDto)
        {
            var procedure = await _hrProcedureRepository.GetByIdAsync(procedureId)
                ?? throw new KeyNotFoundException("HR procedure not found.");

            if (procedure.Status != "Pending")
                throw new InvalidOperationException("Cannot update: only Pending procedures can be updated.");

            var type = updateDto.ProcedureType;

            if (type.Equals("Transfer", StringComparison.OrdinalIgnoreCase))
            {
                bool hasAnyChange = updateDto.NewDepartmentId.HasValue
                    || updateDto.NewPositionId.HasValue
                    || updateDto.NewManagerId.HasValue
                    || updateDto.NewSalary.HasValue;
                if (!hasAnyChange)
                    throw new ArgumentException("Transfer requires at least one of: NewDepartmentId, NewPositionId, NewManagerId, NewSalary.");
            }

            if (type.Equals("Demotion", StringComparison.OrdinalIgnoreCase) && !updateDto.NewPositionId.HasValue)
                throw new ArgumentException("Demotion requires NewPositionId.");

            if (updateDto.NewManagerId.HasValue)
                await ValidateNewManagerAsync(procedure.EmployeeId, updateDto.NewManagerId.Value);

            procedure.ProcedureType   = updateDto.ProcedureType;
            procedure.EffectiveDate   = updateDto.EffectiveDate;
            procedure.NewDepartmentId = updateDto.NewDepartmentId;
            procedure.NewPositionId   = updateDto.NewPositionId;
            procedure.NewManagerId    = updateDto.NewManagerId;
            procedure.NewSalary       = updateDto.NewSalary;
            procedure.Reason          = updateDto.Reason;

            await _hrProcedureRepository.UpdateAsync(procedure);

            return await BuildResponseDtoAsync(procedure);
        }

        // ─────────────────────────────────────────
        // REJECT
        // ─────────────────────────────────────────
        public async Task<HRProcedureResponseDto> RejectProcedureAsync(int procedureId, RejectHRProcedureDto rejectDto)
        {
            var procedure = await _hrProcedureRepository.GetByIdWithDetailsAsync(procedureId)
                ?? throw new KeyNotFoundException("HR procedure not found.");

            if (procedure.Status != "Pending")
                throw new InvalidOperationException("Cannot reject: only Pending procedures can be rejected.");

            if (string.IsNullOrWhiteSpace(rejectDto.RejectionReason))
                throw new ArgumentException("Rejection reason is required.");

            // ── Mới: Thắt chặt quyền từ chối ─────────────────────
            var isAdmin = _currentUserService.RoleName == "ADMIN";
            bool isAuthorized = isAdmin;

            if (!isAuthorized)
            {
                try
                {
                    var currentEmpId = await _currentUserService.GetCurrentEmployeeIdAsync();
                    isAuthorized = await _topLevelResolver.IsTopLevelEmployeeAsync(currentEmpId);
                }
                catch { /* Không phải nhân viên hoặc không tìm thấy profile */ }
            }

            if (!isAuthorized)
            {
                throw new UnauthorizedAccessException("Only ADMIN or TOP-LEVEL employees can reject HR procedures.");
            }
            // ──────────────────────────────────────────────────
            
            var finalReviewerId = isAdmin ? (int?)null : await _currentUserService.GetCurrentEmployeeIdAsync();
            procedure.Status          = "Rejected";
            procedure.RejectionReason = rejectDto.RejectionReason;
            procedure.ReviewedDate    = DateTime.UtcNow;
            procedure.ReviewedBy      = finalReviewerId;

            await _hrProcedureRepository.UpdateAsync(procedure);

            return await BuildResponseDtoAsync(procedure);
        }

        // ─────────────────────────────────────────
        // DELETE
        // ─────────────────────────────────────────
        public async Task<bool> DeleteProcedureAsync(int procedureId)
        {
            var procedure = await _hrProcedureRepository.GetByIdAsync(procedureId);
            if (procedure == null || procedure.Status != "Pending")
                return false;

            return await _hrProcedureRepository.DeleteAsync(procedureId);
        }

        // ─────────────────────────────────────────
        // READ
        // ─────────────────────────────────────────
        public async Task<HRProcedureResponseDto?> GetProcedureByIdAsync(int procedureId)
        {
            var procedure = await _hrProcedureRepository.GetByIdWithDetailsAsync(procedureId);
            if (procedure == null) return null;
            return await BuildResponseDtoAsync(procedure);
        }

        public async Task<IEnumerable<HRProcedureListDto>> GetAllProceduresAsync()
        {
            var isAdmin = _currentUserService.RoleName == "ADMIN";
            var isHR = _currentUserService.RoleName == "HR";
            bool seeAll = isAdmin || isHR;

            if (!seeAll)
            {
                try
                {
                    var currentEmpId = await _currentUserService.GetCurrentEmployeeIdAsync();
                    seeAll = await _topLevelResolver.IsTopLevelEmployeeAsync(currentEmpId);
                }
                catch { /* Không phải nhân viên */ }
            }

            IEnumerable<Hrprocedure> procedures;
            if (seeAll)
            {
                procedures = await _hrProcedureRepository.GetAllAsync();
            }
            else
            {
                // Chỉ xem được đơn của chính mình
                var currentEmpId = await _currentUserService.GetCurrentEmployeeIdAsync();
                procedures = await _hrProcedureRepository.GetByEmployeeIdAsync(currentEmpId);
            }

            return await MapToListDtosAsync(procedures);
        }

        public async Task<IEnumerable<HRProcedureListDto>> GetPendingProceduresAsync()
        {
            // ── Mới: Chỉ ADMIN và TOP-LEVEL mới thấy danh sách chờ duyệt ─────
            var isAdmin = _currentUserService.RoleName == "ADMIN";
            bool isAuthorized = isAdmin;

            if (!isAuthorized)
            {
                try
                {
                    var currentEmpId = await _currentUserService.GetCurrentEmployeeIdAsync();
                    isAuthorized = await _topLevelResolver.IsTopLevelEmployeeAsync(currentEmpId);
                }
                catch { /* Không phải nhân viên */ }
            }

            if (!isAuthorized)
            {
                return Enumerable.Empty<HRProcedureListDto>();
            }
            // ────────────────────────────────────────────────────────────────

            var procedures = await _hrProcedureRepository.GetPendingProceduresAsync();
            return await MapToListDtosAsync(procedures);
        }

        public async Task<IEnumerable<HRProcedureListDto>> GetProceduresByEmployeeAsync(int employeeId)
        {
            if (!await _hrProcedureRepository.EmployeeExistsAsync(employeeId))
                throw new KeyNotFoundException("Employee not found.");

            var procedures = await _hrProcedureRepository.GetByEmployeeIdAsync(employeeId);
            return await MapToListDtosAsync(procedures);
        }

        public async Task<IEnumerable<HRProcedureListDto>> GetProceduresByStatusAsync(string status)
        {
            var procedures = await _hrProcedureRepository.GetByStatusAsync(status);
            return await MapToListDtosAsync(procedures);
        }

        // ═════════════════════════════════════════
        // PRIVATE HELPERS
        // ═════════════════════════════════════════

        /// <summary>Phase 2: nên apply ngay nếu EffectiveDate &lt;= hôm nay</summary>
        private static bool ShouldApplyNow(Hrprocedure procedure)
            => procedure.EffectiveDate <= DateOnly.FromDateTime(AppTime.Today);

        /// <summary>Phase 2: ghi thay đổi vào employee profile</summary>
        private async Task ApplyProcedureToEmployeeAsync(Hrprocedure procedure, int? appliedBy = null)
        {
            var employee = await _employeeRepository.GetEmployeeByIdAsync(procedure.EmployeeId);
            if (employee == null) return;

            switch (procedure.ProcedureType.ToLower())
            {
                case "appointment":
                    if (procedure.NewDepartmentId.HasValue) employee.DepartmentId = procedure.NewDepartmentId.Value;
                    if (procedure.NewPositionId.HasValue)   employee.PositionId   = procedure.NewPositionId.Value;
                    if (procedure.NewManagerId.HasValue)    employee.ManagerId    = procedure.NewManagerId.Value;
                    break;

                case "transfer":
                    if (procedure.NewDepartmentId.HasValue) employee.DepartmentId = procedure.NewDepartmentId.Value;
                    if (procedure.NewPositionId.HasValue)   employee.PositionId   = procedure.NewPositionId.Value;
                    if (procedure.NewManagerId.HasValue)    employee.ManagerId    = procedure.NewManagerId.Value;
                    if (procedure.NewSalary.HasValue)       employee.BaseSalary   = procedure.NewSalary.Value;
                    break;

                case "demotion":
                    if (procedure.NewPositionId.HasValue) employee.PositionId = procedure.NewPositionId.Value;
                    if (procedure.NewSalary.HasValue)     employee.BaseSalary  = procedure.NewSalary.Value;
                    if (procedure.NewManagerId.HasValue)  employee.ManagerId   = procedure.NewManagerId.Value;
                    break;

                case "termination":
                    employee.EmploymentStatus = "Terminated";
                    employee.ResignationDate  = procedure.EffectiveDate;
                    await DeactivateUserAccountAsync(employee);
                    break;
            }

            employee.ModifiedDate = DateTime.UtcNow;
            employee.ModifiedBy   = appliedBy;
            await _employeeRepository.UpdateEmployeeAsync(employee);

            // Tự động cấp/nâng cấp tài khoản nếu vị trí mới là cấp quản lý
            if (procedure.NewPositionId.HasValue &&
                procedure.ProcedureType.ToLower() is "appointment" or "transfer")
            {
                await ProvisionUserAccountAsync(employee, procedure.NewPositionId.Value, procedure.ProcedureType);
            }

            // Giáng chức: gửi mail + hạ quyền về EMPLOYEE nếu vị trí mới là Level 1
            if (procedure.NewPositionId.HasValue &&
                procedure.ProcedureType.ToLower() is "demotion")
            {
                await HandleDemotionAccountAsync(employee, procedure.NewPositionId.Value, procedure.NewSalary);
            }
        }

        /// <summary>Phase 1: phát hiện no-op – tất cả giá trị mới giống hiện tại</summary>
        private async Task ValidateNotNoOpAsync(CreateHRProcedureDto dto)
        {
            var employee = await _employeeRepository.GetEmployeeByIdAsync(dto.EmployeeId);
            if (employee == null) return;

            bool deptSame     = !dto.NewDepartmentId.HasValue || dto.NewDepartmentId == employee.DepartmentId;
            bool positionSame = !dto.NewPositionId.HasValue   || dto.NewPositionId   == employee.PositionId;
            bool managerSame  = !dto.NewManagerId.HasValue    || dto.NewManagerId    == employee.ManagerId;
            bool salarySame   = !dto.NewSalary.HasValue       || dto.NewSalary       == employee.BaseSalary;

            if (deptSame && positionSame && managerSame && salarySame)
                throw new ArgumentException("Procedure is a no-op: all new values are identical to the current employee profile.");
        }

        /// <summary>Phase 1: validate NewManager hợp lệ</summary>
        private async Task ValidateNewManagerAsync(int employeeId, int newManagerId)
        {
            if (newManagerId == employeeId)
                throw new ArgumentException("NewManager cannot be the employee themselves.");

            var manager = await _employeeRepository.GetEmployeeByIdAsync(newManagerId);
            if (manager == null)
                throw new KeyNotFoundException("NewManager not found.");

            if (manager.EmploymentStatus != "Active")
                throw new ArgumentException("NewManager must be an active employee.");
        }

        /// <summary>Build full response DTO, lookup names lazy</summary>
        private async Task<HRProcedureResponseDto> BuildResponseDtoAsync(Hrprocedure p)
        {
            var submittedBy  = await _employeeRepository.GetEmployeeByIdAsync(p.SubmittedBy);
            var reviewedBy   = p.ReviewedBy.HasValue  ? await _employeeRepository.GetEmployeeByIdAsync(p.ReviewedBy.Value)  : null;
            var approvedBy   = p.ApprovedBy.HasValue  ? await _employeeRepository.GetEmployeeByIdAsync(p.ApprovedBy.Value)  : null;
            var appliedBy    = p.AppliedBy.HasValue   ? await _employeeRepository.GetEmployeeByIdAsync(p.AppliedBy.Value)   : null;
            var newManager   = p.NewManagerId.HasValue ? await _employeeRepository.GetEmployeeByIdAsync(p.NewManagerId.Value) : null;

            return new HRProcedureResponseDto
            {
                ProcedureId      = p.ProcedureId,
                ProcedureNumber  = p.ProcedureNumber,
                EmployeeId       = p.EmployeeId,
                EmployeeFullName = p.Employee?.FullName ?? "Unknown",
                EmployeeCode     = p.Employee?.EmployeeCode ?? "",
                ProcedureType    = p.ProcedureType,
                EffectiveDate    = p.EffectiveDate,
                NewDepartmentId  = p.NewDepartmentId,
                NewDepartmentName = p.NewDepartment?.DepartmentName,
                NewPositionId    = p.NewPositionId,
                NewPositionName  = p.NewPosition?.PositionName,
                NewManagerId     = p.NewManagerId,
                NewManagerName   = newManager?.FullName,
                NewSalary        = p.NewSalary,
                Reason           = p.Reason,
                Status           = p.Status,
                RejectionReason  = p.RejectionReason,
                SubmittedDate    = p.SubmittedDate,
                SubmittedBy      = p.SubmittedBy,
                SubmittedByName  = submittedBy?.FullName  ?? "System",
                ReviewedDate     = p.ReviewedDate,
                ReviewedBy       = p.ReviewedBy,
                ReviewedByName   = reviewedBy?.FullName   ?? null,
                ApprovedDate     = p.ApprovedDate,
                ApprovedBy       = p.ApprovedBy,
                ApprovedByName   = approvedBy?.FullName   ?? null,
                AppliedDate      = p.AppliedDate,
                AppliedBy        = p.AppliedBy,
                AppliedByName    = appliedBy?.FullName    ?? null,
            };
        }

        private async Task<IEnumerable<HRProcedureListDto>> MapToListDtosAsync(IEnumerable<Hrprocedure> procedures)
        {
            var employees = await _employeeRepository.GetAllEmployeesAsync();
            return procedures.Select(p =>
            {
                var submittedByEmployee = employees.FirstOrDefault(e => e.EmployeeId == p.SubmittedBy);
                return new HRProcedureListDto
                {
                    ProcedureId      = p.ProcedureId,
                    ProcedureNumber  = p.ProcedureNumber,
                    EmployeeFullName = p.Employee?.FullName ?? "Unknown",
                    EmployeeCode     = p.Employee?.EmployeeCode ?? "",
                    ProcedureType    = p.ProcedureType,
                    EffectiveDate    = p.EffectiveDate,
                    Status           = p.Status,
                    SubmittedDate    = p.SubmittedDate,
                    SubmittedByName  = submittedByEmployee?.FullName ?? "System"
                };
            }).ToList();
        }

        private static string GenerateProcedureNumber()
        {
            var datePrefix = AppTime.Now.ToString("yyyyMMdd");
            var random = new Random().Next(1000, 9999);
            return $"PR-{datePrefix}-{random}";
        }

        // ─────────────────────────────────────────────────────────────────────
        // AUTO PROVISIONING HELPERS
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Xử lý tài khoản khi giáng chức:
        /// - Luôn gửi email thông báo giáng chức cho nhân viên.
        /// - Nếu vị trí mới là Level 1 (không phải quản lý), hạ role về EMPLOYEE.
        /// </summary>
        private async Task HandleDemotionAccountAsync(Employee employee, int newPositionId, decimal? newSalary)
        {
            var newPosition = await _context.Positions.FindAsync(newPositionId);
            if (newPosition == null) return;

            bool isDowngradeToEmployee = newPosition.Level < 2 && !newPosition.IsTopLevel;

            if (isDowngradeToEmployee)
            {
                var employeeRole = await _context.Roles
                    .FirstOrDefaultAsync(r => r.RoleName == "EMPLOYEE" && r.IsActive);

                if (employeeRole != null)
                {
                    var existingUser = await _context.Users
                        .Include(u => u.UserRoles)
                        .FirstOrDefaultAsync(u => u.EmployeeId == employee.EmployeeId && u.IsActive);

                    if (existingUser != null)
                    {
                        _context.UserRoles.RemoveRange(existingUser.UserRoles);
                        _context.UserRoles.Add(new UserRole
                        {
                            UserId = existingUser.UserId,
                            RoleId = employeeRole.RoleId
                        });
                        await _context.SaveChangesAsync();
                    }
                }
            }

            var salaryLine = newSalary.HasValue
                ? $"<li><b>Mức lương mới:</b> {newSalary.Value:N0} VND</li>"
                : string.Empty;

            var roleNote = isDowngradeToEmployee
                ? "<p><b>Lưu ý:</b> Quyền hạn tài khoản của bạn đã được điều chỉnh về mức Nhân viên. Vui lòng đăng nhập lại để áp dụng.</p>"
                : string.Empty;

            var demotionBody = $@"
                <h3>Thông báo: Quyết định Giáng chức</h3>
                <p>Xin chào <b>{employee.FullName}</b>,</p>
                <p>Bộ phận Nhân sự thông báo bạn có quyết định <b>Giáng chức</b> với các thông tin sau:</p>
                <ul>
                    <li><b>Vị trí mới:</b> {newPosition.PositionName}</li>
                    {salaryLine}
                </ul>
                {roleNote}
                <p>Nếu có thắc mắc, vui lòng liên hệ bộ phận Nhân sự.</p>";

            await _emailService.SendAsync(
                employee.Email,
                "Thông báo quyết định Giáng chức",
                demotionBody);
        }

        /// <summary>
        /// Tự động tạo tài khoản mới hoặc nâng cấp Role khi nhân viên được
        /// bổ nhiệm / điều chuyển sang vị trí cấp quản lý
        /// (Position.Level >= 2 hoặc IsTopLevel = true).
        /// </summary>
        private async Task ProvisionUserAccountAsync(Employee employee, int newPositionId, string procedureType)
        {
            var newPosition = await _context.Positions.FindAsync(newPositionId);
            if (newPosition == null) return;

            // Chỉ xử lý khi vị trí mới là cấp quản lý trở lên
            bool needsManagerRole = newPosition.Level >= 2 || newPosition.IsTopLevel;
            if (!needsManagerRole) return;

            // Xác định Role phù hợp: Luôn là MANAGE theo yêu cầu
            string targetRoleName = "MANAGE";
            var targetRole = await _context.Roles
                .FirstOrDefaultAsync(r => r.RoleName == targetRoleName && r.IsActive);
            if (targetRole == null) return; // Role chưa được cấu hình trong DB

            // Kiểm tra nhân viên đã có tài khoản active chưa
            var existingUser = await _context.Users
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.EmployeeId == employee.EmployeeId && u.IsActive);

            if (existingUser != null)
            {
                // Kiểm tra xem đã có đúng role MANAGE chưa
                bool hasOnlyTargetRole = existingUser.UserRoles.Count == 1 && existingUser.UserRoles.Any(ur => ur.RoleId == targetRole.RoleId);
                
                if (!hasOnlyTargetRole)
                {
                    // Xóa tất cả Role hiện tại để đảm bảo chỉ có 1 Role duy nhất
                    _context.UserRoles.RemoveRange(existingUser.UserRoles);
                    
                    _context.UserRoles.Add(new UserRole
                    {
                        UserId = existingUser.UserId,
                        RoleId = targetRole.RoleId
                    });
                    
                    await _context.SaveChangesAsync();

                    // Gửi email thông báo cập nhật quyền
                    var upgradeBody = $@"
                        <h3>Cập nhật quyền hạn tài khoản HR System</h3>
                        <p>Xin chào <b>{employee.FullName}</b>,</p>
                        <p>Tài khoản của bạn vừa được cập nhật quyền hạn mới sau quyết định <b>{GetProcedureTypeVN(procedureType)}</b>:</p>
                        <ul>
                            <li><b>Vị trí mới:</b> {newPosition.PositionName}</li>
                            <li><b>Quyền hạn hiện tại:</b> {targetRoleName}</li>
                        </ul>
                        <p>Ghi chú: Mọi quyền hạn cũ đã được thay thế bằng quyền hạn Quản lý. Vui lòng đăng nhập lại để áp dụng.</p>";

                    await _emailService.SendAsync(
                        employee.Email,
                        "Cập nhật quyền hạn tài khoản HR System",
                        upgradeBody);
                }
            }
            else
            {
                // TH2: Chưa có tài khoản → Tạo mới
                var baseUsername = employee.EmployeeCode.ToLowerInvariant();
                var username = baseUsername;
                var suffix = 1;
                while (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Username == username))
                    username = $"{baseUsername}{suffix++}";

                var tempPassword = Guid.NewGuid().ToString("N")[..8];

                var newUser = new User
                {
                    Username     = username,
                    Email        = employee.Email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword),
                    EmployeeId   = employee.EmployeeId,
                    IsActive     = true,
                    CreatedDate  = DateTime.UtcNow
                };

                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();

                _context.UserRoles.Add(new UserRole
                {
                    UserId = newUser.UserId,
                    RoleId = targetRole.RoleId
                });
                await _context.SaveChangesAsync();

                var newAccountBody = $@"
                    <h3>Tài khoản HR System đã được tạo</h3>
                    <p>Xin chào <b>{employee.FullName}</b>,</p>
                    <p>Chúc mừng bạn đã được <b>{GetProcedureTypeVN(procedureType)}</b> lên vị trí <b>{newPosition.PositionName}</b>.</p>
                    <p>Hệ thống đã tự động tạo tài khoản để bạn thực hiện các nghiệp vụ quản lý:</p>
                    <ul>
                        <li><b>Username:</b> {username}</li>
                        <li><b>Mật khẩu tạm:</b> {tempPassword}</li>
                        <li><b>Vai trò:</b> {targetRoleName}</li>
                    </ul>
                    <p><b>Vui lòng đăng nhập và đổi mật khẩu ngay sau khi nhận được email này.</b></p>";

                await _emailService.SendAsync(
                    employee.Email,
                    "Tài khoản HR System của bạn đã được kích hoạt",
                    newAccountBody);
            }
        }

        /// <summary>
        /// Tự động vô hiệu hóa tài khoản khi nhân viên thôi việc hoặc bị sa thải.
        /// </summary>
        private async Task DeactivateUserAccountAsync(Employee employee)
        {
            var users = await _context.Users
                .Where(u => u.EmployeeId == employee.EmployeeId && u.IsActive)
                .ToListAsync();

            if (!users.Any()) return;

            foreach (var user in users)
            {
                user.IsActive     = false;
                user.ModifiedDate = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();

            var deactivateBody = $@"
                <h3>Thông báo: Tài khoản HR System đã bị vô hiệu hóa</h3>
                <p>Xin chào <b>{employee.FullName}</b>,</p>
                <p>Do thủ tục nhân sự liên quan đến việc chấm dứt hợp đồng,
                   tài khoản của bạn đã bị <b>vô hiệu hóa</b>.</p>
                <p>Nếu có thắc mắc, vui lòng liên hệ bộ phận Nhân sự.</p>";

            await _emailService.SendAsync(
                employee.Email,
                "Tài khoản HR System đã bị vô hiệu hóa",
                deactivateBody);
        }

        private static string GetProcedureTypeVN(string type) => type.ToLower() switch
        {
            "appointment" => "Bổ nhiệm",
            "transfer"    => "Điều chuyển",
            "demotion"    => "Giáng chức",
            "termination" => "Sa thải",
            _             => type
        };
    }
}
