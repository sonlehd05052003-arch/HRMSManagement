using HRManagement.DataAcess.Interfaces;
using HRManagement.DTOs;
using HRManagement.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace HRManagement.Services.Employees
{
    public class EmployeeService : IEmployeeService
    {
        private readonly IEmployeeRepository _employeeRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly Approvals.IApprovalRouteService _approvalRouteService;
        private readonly Approvals.ITopLevelResolver _topLevelResolver;
        private readonly HRManagement.Models.HrmsDbContext _context;

        public EmployeeService(
            IEmployeeRepository employeeRepository, 
            IHttpContextAccessor httpContextAccessor,
            Approvals.IApprovalRouteService approvalRouteService,
            Approvals.ITopLevelResolver topLevelResolver,
            HRManagement.Models.HrmsDbContext context)
        {
            _employeeRepository = employeeRepository;
            _httpContextAccessor = httpContextAccessor;
            _approvalRouteService = approvalRouteService;
            _topLevelResolver = topLevelResolver;
            _context = context;
        }

        private async Task<string> GenerateEmployeeCodeAsync()
        {
            int number = 1;

            while (true)
            {
                string code = $"EMP{number:D3}"; // EMP001, EMP002...

                bool exists = await _employeeRepository.EmployeeCodeExistsAsync(code);

                if (!exists)
                    return code;

                number++;
            }
        }
        public async Task<EmployeeResponseDetailDto> AddEmployeeAsync(CreateEmployeeDto dto)
        {
            var today = DateOnly.FromDateTime(AppTime.Today);

            if (await _employeeRepository.EmailExistsAsync(dto.Email))
                throw new InvalidOperationException($"Email '{dto.Email}' already exists.");


            if (dto.BaseSalary < 0)
                throw new ArgumentException("Base salary cannot be negative.");


            if (dto.JoinDate < today)
                throw new ArgumentException("Join date cannot be in the past.");

            if (dto.DateOfBirth is DateOnly dob)
            {
                if (dob > today)
                    throw new ArgumentException("Date of birth cannot be in the future.");

                if (dto.JoinDate <= dob)
                    throw new ArgumentException("Join date must be after date of birth.");

                if (dto.JoinDate < dob.AddYears(18))
                    throw new ArgumentException("Employee must be at least 18 years old at the time of joining.");
            }
            if (dto.DepartmentId.HasValue)
            {
                var departmentExists = await _employeeRepository.DepartmentExistsAsync(dto.DepartmentId.Value);

                if (!departmentExists)
                    throw new ArgumentException($"Department {dto.DepartmentId} does not exist.");
            }


            if (dto.PositionId.HasValue)
            {
                var positionExists = await _employeeRepository.PositionExistsAsync(dto.PositionId.Value);

                if (!positionExists)
                    throw new ArgumentException($"Position {dto.PositionId} does not exist.");
            }


            var validTypes = new[]
            {
        "Full-Time", "Part-Time", "Contract", "Intern"
    };

            if (!validTypes.Contains(dto.EmploymentType))
                throw new ArgumentException(
                    "Invalid employment type. Allowed values: Full-Time, Part-Time, Contract, Intern."
                );

            var validStatus = new[]
            {
        "Active", "Resigned", "Terminated", "On Leave", "Suspended", "Inactive"
    };

            if (!validStatus.Contains(dto.EmploymentStatus))
                throw new ArgumentException(
                    "Invalid employment status. Allowed values: Active, Resigned, Terminated, On Leave, Suspended, Inactive"
                );

            int createdBy = GetCurrentUserId(dto.CreatedBy);
            string employeeCode = await GenerateEmployeeCodeAsync();

            var employee = new Employee
            {
                EmployeeCode = employeeCode,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                FullName = $"{dto.FirstName} {dto.LastName}",
                Email = dto.Email,
                Phone = dto.Phone,
                DateOfBirth = dto.DateOfBirth,
                Gender = dto.Gender,
                Address = dto.Address,
                City = dto.City,
                Country = dto.Country,
                DepartmentId = dto.DepartmentId,
                PositionId = dto.PositionId,
                ManagerId = dto.ManagerId,
                JoinDate = dto.JoinDate,
                EmploymentStatus = dto.EmploymentStatus,
                EmploymentType = dto.EmploymentType,
                BaseSalary = dto.BaseSalary,
                InsuranceSalary = dto.InsuranceSalary,
                NumberOfDependents = dto.NumberOfDependents,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = createdBy
            };

            await _employeeRepository.AddEmployeeAsync(employee);

            return new EmployeeResponseDetailDto
            {
                EmployeeId = employee.EmployeeId,
                EmployeeCode = employee.EmployeeCode,
                FirstName = employee.FirstName,
                LastName = employee.LastName,
                Email = employee.Email,
                Phone = employee.Phone,
                DateOfBirth = employee.DateOfBirth,
                Gender = employee.Gender,
                Address = employee.Address,
                City = employee.City,
                Country = employee.Country,
                DepartmentId = employee.DepartmentId ?? null,
                DepartmentName = employee.Department?.DepartmentName ?? "N/A",
                PositionId = employee.PositionId ?? null,
                PositionName = employee.Position?.PositionName ?? "N/A",
                ManagerId = employee.ManagerId ?? null,
                ManagerName = employee.Manager is null ? "N/A" : $"{employee.Manager.FirstName} {employee.Manager.LastName}",
                JoinDate = employee.JoinDate,
                ResignationDate = employee.ResignationDate,
                EmploymentStatus = employee.EmploymentStatus,
                EmploymentType = employee.EmploymentType,
                BaseSalary = employee.BaseSalary,
                InsuranceSalary = employee.InsuranceSalary,
                NumberOfDependents = employee.NumberOfDependents,
            };
        }


        public async Task<bool> UpdateStatusAsync(int id, string status, int? modifiedBy = null)
        {
            var validStatuses = new[]
            {
                "Active",
                "Inactive",
                "Resigned",
                "Terminated",
                "On Leave",
                "Suspended"
            };

            if (!validStatuses.Contains(status))
                throw new ArgumentException("Invalid employment status.");

            var employee = await _employeeRepository.GetEmployeeByIdAsync(id);

            if (employee == null)
                return false;

            if (status == "Resigned" || status == "Terminated" || status == "Inactive")
                employee.ResignationDate = DateOnly.FromDateTime(AppTime.Today);

            if (status == "Active")
                employee.ResignationDate = null;

            employee.EmploymentStatus = status;
            employee.ModifiedDate = DateTime.UtcNow;
            employee.ModifiedBy = modifiedBy;

            await _employeeRepository.UpdateEmployeeAsync(employee);

            // Update associated user status
            var user = await _context.Users.FirstOrDefaultAsync(u => u.EmployeeId == id);
            if (user != null)
            {
                user.IsActive = status == "Active";
                user.ModifiedDate = DateTime.UtcNow;
                user.ModifiedBy = modifiedBy;
                await _context.SaveChangesAsync();
            }

            return true;
        }

        public async Task<IEnumerable<EmployeeResponseListDto>> GetAllEmployeesAsync()
        {
            var employees = await _employeeRepository.GetAllEmployeesAsync();
            return employees.Select(e => new EmployeeResponseListDto
            {
                EmployeeId = e.EmployeeId,
                EmployeeCode = e.EmployeeCode,
                FullName = e.FullName, 
                Email = e.Email,
                Phone = e.Phone,
                Gender = e.Gender,
                EmploymentStatus = e.EmploymentStatus,
                JoinDate = e.JoinDate,
                DepartmentName = e.Department?.DepartmentName ?? "N/A",
                PositionName = e.Position?.PositionName ?? "N/A",
                ManagerId = e.ManagerId,
                ManagerName = e.Manager == null ? null : $"{e.Manager.FirstName} {e.Manager.LastName}",
                DepartmentId = e.DepartmentId,
                RoleName = e.Users.FirstOrDefault()?.UserRoles.FirstOrDefault()?.Role.RoleName
            }).ToList();
        }

        public async Task<IEnumerable<EmployeeResponseListDto>> GetActiveEmployeesAsync()
        {
            var employees = await _employeeRepository.GetActiveEmployeesAsync();
            return employees.Select(e => new EmployeeResponseListDto
            {
                EmployeeId = e.EmployeeId,
                EmployeeCode = e.EmployeeCode,
                FullName = e.FullName,
                Email = e.Email,
                Phone = e.Phone,
                Gender = e.Gender,
                EmploymentStatus = e.EmploymentStatus,
                JoinDate = e.JoinDate,
                DepartmentName = e.Department?.DepartmentName ?? "N/A",
                PositionName = e.Position?.PositionName ?? "N/A",
                ManagerId = e.ManagerId,
                ManagerName = e.Manager == null ? null : $"{e.Manager.FirstName} {e.Manager.LastName}",
                DepartmentId = e.DepartmentId,
                RoleName = e.Users.FirstOrDefault()?.UserRoles.FirstOrDefault()?.Role.RoleName
            }).ToList();
        }

        public async Task<EmployeeResponseDetailDto?> GetEmployeeByIdAsync(int employeeId)
        {
            var employee = await _employeeRepository.GetEmployeeByIdAsync(employeeId);
            if (employee is null) return null;

            return new EmployeeResponseDetailDto
            {
                EmployeeId = employee.EmployeeId,
                EmployeeCode = employee.EmployeeCode,
                FirstName = employee.FirstName,
                LastName = employee.LastName,
                Email = employee.Email,
                Phone = employee.Phone,
                DateOfBirth = employee.DateOfBirth,
                Gender = employee.Gender,
                Address = employee.Address,
                City = employee.City,
                Country = employee.Country,
                DepartmentId = employee.DepartmentId ?? null,
                DepartmentName = employee.Department?.DepartmentName ?? "N/A",
                PositionId = employee.PositionId ?? null,
                PositionName = employee.Position?.PositionName ?? "N/A",
                ManagerId = employee.ManagerId ?? null,
                ManagerName = employee.Manager is null ? "N/A" : $"{employee.Manager.FirstName} {employee.Manager.LastName}",
                JoinDate = employee.JoinDate,
                ResignationDate = employee.ResignationDate,
                EmploymentStatus = employee.EmploymentStatus,
                EmploymentType = employee.EmploymentType,
                BaseSalary = employee.BaseSalary,
                InsuranceSalary = employee.InsuranceSalary,
                NumberOfDependents = employee.NumberOfDependents,
            };
        }

        public async Task<EmployeeResponseDetailDto> UpdateEmployeeAsync(int employeeId, UpdateEmployeeDto dto)
        {
            var employee = await _employeeRepository.GetEmployeeByIdAsync(employeeId);

            if (employee == null)
                throw new KeyNotFoundException($"Employee {employeeId} not found.");

            var today = DateOnly.FromDateTime(AppTime.Today);

            if (dto.BaseSalary < 0)
                throw new ArgumentException("Base salary cannot be negative.");


            if (dto.ResignationDate.HasValue && dto.ResignationDate.Value < dto.JoinDate)
                throw new ArgumentException("Resignation date cannot be before join date.");

            if (dto.ManagerId.HasValue && dto.ManagerId == employeeId)
                throw new ArgumentException("Employee cannot be their own manager.");

            if (dto.DateOfBirth.HasValue)
            {
                if (dto.DateOfBirth.Value > today)
                    throw new ArgumentException("Date of birth cannot be in the future.");

                if (dto.JoinDate <= dto.DateOfBirth.Value)
                    throw new ArgumentException("Join date must be after date of birth.");

                if (dto.JoinDate < dto.DateOfBirth.Value.AddYears(18))
                    throw new ArgumentException("Employee must be at least 18 years old at the time of joining.");
            }

            if (dto.DepartmentId.HasValue)
            {
                var departmentExists = await _employeeRepository.DepartmentExistsAsync(dto.DepartmentId.Value);

                if (!departmentExists)
                    throw new ArgumentException($"Department {dto.DepartmentId} does not exist.");
            }

            if (dto.PositionId.HasValue)
            {
                var positionExists = await _employeeRepository.PositionExistsAsync(dto.PositionId.Value);

                if (!positionExists)
                    throw new ArgumentException($"Position {dto.PositionId} does not exist.");
            }

            var validTypes = new[]
            {
        "Full-Time", "Part-Time", "Contract", "Intern"
    };

            if (!validTypes.Contains(dto.EmploymentType))
            {
                throw new ArgumentException(
                    "Invalid employment type. Allowed values: Full-Time, Part-Time, Contract, Intern."
                );
            }

            var validStatus = new[]
            {
        "Active", "Resigned", "Terminated", "On Leave", "Suspended", "Inactive"
    };

            if (!validStatus.Contains(dto.EmploymentStatus))
            {
                throw new ArgumentException(
                    "Invalid employment status. Allowed values: Active, Resigned, Terminated, On Leave, Suspended, Inactive."
                );
            }

            // KHÔNG update EmployeeCode nữa
            employee.FirstName = dto.FirstName;
            employee.LastName = dto.LastName;
            employee.FullName = $"{dto.FirstName} {dto.LastName}";
            employee.Email = dto.Email;
            employee.Phone = dto.Phone;
            employee.DateOfBirth = dto.DateOfBirth;
            employee.Gender = dto.Gender;
            employee.Address = dto.Address;
            employee.City = dto.City;
            employee.Country = dto.Country;
            employee.DepartmentId = dto.DepartmentId;
            employee.PositionId = dto.PositionId;
            employee.ManagerId = dto.ManagerId;
            employee.JoinDate = dto.JoinDate;
            employee.ResignationDate = dto.ResignationDate;
            employee.EmploymentStatus = dto.EmploymentStatus;
            employee.EmploymentType = dto.EmploymentType;
            employee.BaseSalary = dto.BaseSalary;
            employee.InsuranceSalary = dto.InsuranceSalary;
            employee.NumberOfDependents = dto.NumberOfDependents;
            employee.ModifiedDate = DateTime.UtcNow;
            employee.ModifiedBy = GetCurrentUserId(dto.ModifiedBy);

            await _employeeRepository.UpdateEmployeeAsync(employee);

            return new EmployeeResponseDetailDto
            {
                EmployeeId = employee.EmployeeId,
                EmployeeCode = employee.EmployeeCode,
                FirstName = employee.FirstName,
                LastName = employee.LastName,
                Email = employee.Email,
                Phone = employee.Phone,
                DateOfBirth = employee.DateOfBirth,
                Gender = employee.Gender,
                Address = employee.Address,
                City = employee.City,
                Country = employee.Country,
                DepartmentId = employee.DepartmentId ?? null,
                DepartmentName = employee.Department?.DepartmentName ?? "N/A",
                PositionId = employee.PositionId ?? null,
                PositionName = employee.Position?.PositionName ?? "N/A",
                ManagerId = employee.ManagerId ?? null,
                ManagerName = employee.Manager is null ? "N/A" : $"{employee.Manager.FirstName} {employee.Manager.LastName}",
                JoinDate = employee.JoinDate,
                ResignationDate = employee.ResignationDate,
                EmploymentStatus = employee.EmploymentStatus,
                EmploymentType = employee.EmploymentType,
                BaseSalary = employee.BaseSalary,
                InsuranceSalary = employee.InsuranceSalary,
                NumberOfDependents = employee.NumberOfDependents,
            };
        }
        public async Task<IEnumerable<EmployeeApprovalAnalysisDto>> GetApprovalAnalysisAsync()
        {
            var employees = await _context.Employees
                .Include(e => e.Manager)
                .Include(e => e.Position)
                .ToListAsync();

            var result = new List<EmployeeApprovalAnalysisDto>();

            foreach (var e in employees)
            {
                var isTopLevel = e.Position?.IsTopLevel ?? false;
                var approverUserId = await _approvalRouteService.GetApproverIdAsync(e.EmployeeId);
                
                string routeType = "None";
                string? approverName = null;

                if (approverUserId.HasValue)
                {
                    var approverUser = await _context.Users
                        .Include(u => u.Employee)
                        .FirstOrDefaultAsync(u => u.UserId == approverUserId.Value);
                    
                    if (approverUser != null)
                    {
                        approverName = approverUser.Username;
                        if (approverUser.Employee != null)
                        {
                            approverName = $"{approverUser.Employee.FullName} ({approverUser.Username})";
                        }
                    }
                    else
                    {
                        approverName = "Unknown User";
                    }

                    // Determine type based on precedence
                    if (e.ManagerId.HasValue)
                    {
                        routeType = "Direct";
                    }
                    else if (isTopLevel)
                    {
                        routeType = "TopLevelFallback";
                    }
                    else
                    {
                        var defaultSetting = await _topLevelResolver.GetDefaultFallbackUserIdAsync();
                        if (defaultSetting.HasValue && defaultSetting.Value == approverUserId)
                        {
                            routeType = "DefaultFallback";
                        }
                        else
                        {
                            routeType = "SystemAdminFallback";
                        }
                    }
                }

                result.Add(new EmployeeApprovalAnalysisDto
                {
                    EmployeeId = e.EmployeeId,
                    EmployeeCode = e.EmployeeCode,
                    FullName = e.FullName,
                    ManagerId = e.ManagerId,
                    ManagerName = e.Manager?.FullName,
                    IsTopLevel = isTopLevel,
                    TargetApproverId = approverUserId,
                    TargetApproverName = approverName,
                    ApprovalRouteType = routeType,
                    IsValid = approverUserId.HasValue
                });
            }

            return result;
        }

        private int GetCurrentUserId(int? createdBy)
        {
            if (createdBy.HasValue)
                return createdBy.Value;

            var userIdClaim = _httpContextAccessor.HttpContext?
                .User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;

            if (int.TryParse(userIdClaim, out int userId))
                return userId;

            throw new ArgumentException("Cannot determine CreatedBy user.");
        }
    }
}
