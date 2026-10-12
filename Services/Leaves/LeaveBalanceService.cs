using HRManagement.DTOs.LeaveBalance;
using HRManagement.DTOs.LeaveRequest;
using HRManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Services.Leaves
{
    public class LeaveBalanceService : ILeaveBalanceService
    {
        private readonly HrmsDbContext _context;

        public LeaveBalanceService(HrmsDbContext context)
        {
            _context = context;
        }

        public async Task<ServiceResult<List<MyLeaveBalanceDTO>>> GetMyLeaveBalanceAsync(int userId)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);

            if (user == null || user.EmployeeId == null)
            {
                return ServiceResult<List<MyLeaveBalanceDTO>>
                    .Fail("MSG-106", "Access denied.");
            }

            var balances = await _context.LeaveBalances
                .Where(x => x.EmployeeId == user.EmployeeId)
                .Join(
                    _context.LeaveTypes,
                    lb => lb.LeaveTypeId,
                    lt => lt.LeaveTypeId,
                    (lb, lt) => new MyLeaveBalanceDTO
                    {
                        LeaveTypeId = lt.LeaveTypeId,
                        LeaveTypeName = lt.LeaveTypeName,
                        TotalEntitlement = lb.TotalEntitlement,
                        UsedDays = lb.UsedDays,
                        RemainingDays = lb.RemainingDays ?? 0,
                        CarriedForward = lb.CarriedForward,
                        Year = lb.Year
                    })
                .OrderByDescending(x => x.Year)
                .ThenBy(x => x.LeaveTypeName)
                .ToListAsync();

            return ServiceResult<List<MyLeaveBalanceDTO>>
                .Ok("MSG-01", "Success", balances);
        }

        public async Task<ServiceResult<List<LeaveBalanceListDTO>>> GetAllLeaveBalancesAsync()
        {
            var balances = await _context.LeaveBalances
                .Include(x => x.Employee)
                .Include(x => x.LeaveType)
                .Select(x => new LeaveBalanceListDTO
                {
                    BalanceId = x.BalanceId,
                    EmployeeId = x.EmployeeId,
                    EmployeeName = x.Employee.FullName,
                    LeaveTypeId = x.LeaveTypeId,
                    LeaveTypeName = x.LeaveType.LeaveTypeName,
                    Year = x.Year,
                    TotalEntitlement = x.TotalEntitlement,
                    UsedDays = x.UsedDays,
                    RemainingDays = x.RemainingDays ?? 0,
                    CarriedForward = x.CarriedForward,
                    LastUpdated = x.LastUpdated
                })
                .OrderByDescending(x => x.Year)
                .ThenBy(x => x.EmployeeName)
                .ThenBy(x => x.LeaveTypeName)
                .ToListAsync();

            return ServiceResult<List<LeaveBalanceListDTO>>
                .Ok("MSG-01", "Success", balances);
        }

        public async Task<ServiceResult<List<LeaveBalanceListDTO>>> GetLeaveBalancesByEmployeeAsync(int employeeId)
        {
            var employeeExists = await _context.Employees.AnyAsync(x => x.EmployeeId == employeeId);

            if (!employeeExists)
            {
                return ServiceResult<List<LeaveBalanceListDTO>>
                    .Fail("MSG-02", "Employee not found.");
            }

            var balances = await _context.LeaveBalances
                .Where(x => x.EmployeeId == employeeId)
                .Include(x => x.Employee)
                .Include(x => x.LeaveType)
                .Select(x => new LeaveBalanceListDTO
                {
                    BalanceId = x.BalanceId,
                    EmployeeId = x.EmployeeId,
                    EmployeeName = x.Employee.FullName,
                    LeaveTypeId = x.LeaveTypeId,
                    LeaveTypeName = x.LeaveType.LeaveTypeName,
                    Year = x.Year,
                    TotalEntitlement = x.TotalEntitlement,
                    UsedDays = x.UsedDays,
                    RemainingDays = x.RemainingDays ?? 0,
                    CarriedForward = x.CarriedForward,
                    LastUpdated = x.LastUpdated
                })
                .OrderByDescending(x => x.Year)
                .ThenBy(x => x.LeaveTypeName)
                .ToListAsync();

            return ServiceResult<List<LeaveBalanceListDTO>>
                .Ok("MSG-01", "Success", balances);
        }

        public async Task<ServiceResult<string>> CreateLeaveBalanceAsync(int hrUserId, CreateLeaveBalanceDTO dto)
        {
            // Validate employee
            var employeeExists = await _context.Employees.AnyAsync(x => x.EmployeeId == dto.EmployeeId);
            if (!employeeExists)
            {
                return ServiceResult<string>.Fail("MSG-02", "Employee not found.");
            }

            // Validate leave type
            var leaveTypeExists = await _context.LeaveTypes.AnyAsync(x => x.LeaveTypeId == dto.LeaveTypeId);
            if (!leaveTypeExists)
            {
                return ServiceResult<string>.Fail("MSG-03", "Leave type not found.");
            }

            // Validate year
            if (dto.Year < 2000 || dto.Year > AppTime.Now.Year + 5)
            {
                return ServiceResult<string>.Fail("MSG-04", "Invalid year.");
            }

            // Validate numbers
            if (dto.TotalEntitlement < 0 || dto.UsedDays < 0 || dto.CarriedForward < 0)
            {
                return ServiceResult<string>.Fail("MSG-05", "Values cannot be negative.");
            }

            var remainingDays = dto.TotalEntitlement + dto.CarriedForward - dto.UsedDays;

            if (remainingDays < 0)
            {
                return ServiceResult<string>.Fail("MSG-06", "Used days cannot exceed total entitlement plus carried forward.");
            }

            // Check duplicate
            var exists = await _context.LeaveBalances.AnyAsync(x =>
                x.EmployeeId == dto.EmployeeId &&
                x.LeaveTypeId == dto.LeaveTypeId &&
                x.Year == dto.Year);

            if (exists)
            {
                return ServiceResult<string>.Fail("MSG-07", "Leave balance already exists for this employee, leave type, and year.");
            }

            var newBalance = new LeaveBalance
            {
                EmployeeId = dto.EmployeeId,
                LeaveTypeId = dto.LeaveTypeId,
                Year = dto.Year,
                TotalEntitlement = dto.TotalEntitlement,
                UsedDays = dto.UsedDays,
                CarriedForward = dto.CarriedForward,
                RemainingDays = remainingDays,
                LastUpdated = AppTime.Now
            };

            _context.LeaveBalances.Add(newBalance);
            await _context.SaveChangesAsync();

            return ServiceResult<string>.Ok("MSG-08", "Leave balance created successfully.", null);
        }

        public async Task<ServiceResult<string>> AdjustLeaveBalanceAsync(int hrUserId, AdjustLeaveBalanceDTO dto)
        {
            if (dto.NumberOfDays <= 0)
            {
                return ServiceResult<string>.Fail("MSG-09", "Number of days must be greater than 0.");
            }

            var balance = await _context.LeaveBalances
                .FirstOrDefaultAsync(x =>
                    x.EmployeeId == dto.EmployeeId &&
                    x.LeaveTypeId == dto.LeaveTypeId &&
                    x.Year == AppTime.Now.Year);

            if (balance == null)
            {
                return ServiceResult<string>.Fail("MSG-104", "Leave balance not found.");
            }

            var adjustmentType = dto.AdjustmentType?.Trim().ToLower();
            var currentRemaining = balance.RemainingDays ?? 0;

            if (adjustmentType == "add")
            {
                balance.TotalEntitlement += dto.NumberOfDays;
                balance.RemainingDays = currentRemaining + dto.NumberOfDays;
            }
            else if (adjustmentType == "deduct")
            {
                var newTotal = balance.TotalEntitlement - dto.NumberOfDays;
                var newRemaining = currentRemaining - dto.NumberOfDays;

                if (newTotal < balance.UsedDays)
                {
                    return ServiceResult<string>.Fail("MSG-10", "Cannot reduce entitlement below used leave days.");
                }

                if (newRemaining < 0)
                {
                    return ServiceResult<string>.Fail("MSG-11", "Insufficient remaining leave days.");
                }

                balance.TotalEntitlement = newTotal;
                balance.RemainingDays = newRemaining;
            }
            else
            {
                return ServiceResult<string>.Fail("MSG-49", "Invalid adjustment type. Use 'Add' or 'Deduct'.");
            }

            balance.LastUpdated = AppTime.Now;


            await _context.SaveChangesAsync();

            return ServiceResult<string>.Ok("MSG-47", "Leave balance updated successfully.", null);
        }

        public async Task<ServiceResult<GenerateBalanceResultDTO>> GenerateBalancesForYearAsync(int hrUserId, int year, bool carryForward)
        {
            if (year < 2000 || year > AppTime.Now.Year + 5)
                return ServiceResult<GenerateBalanceResultDTO>.Fail("MSG-04", "Invalid year.");

            var activeEmployees = await _context.Employees
                .Where(e => e.EmploymentStatus == "Active")
                .ToListAsync();

            var activeLeaveTypes = await _context.LeaveTypes
                .Where(lt => lt.IsActive && lt.AnnualEntitlement > 0)
                .ToListAsync();

            if (!activeLeaveTypes.Any())
                return ServiceResult<GenerateBalanceResultDTO>.Fail("MSG-03", "No active leave types found.");

            int created = 0;
            int skipped = 0;
            int carriedForwardCount = 0;

            foreach (var employee in activeEmployees)
            {
                foreach (var leaveType in activeLeaveTypes)
                {
                    // Skip if balance already exists for this year
                    var alreadyExists = await _context.LeaveBalances.AnyAsync(x =>
                        x.EmployeeId == employee.EmployeeId &&
                        x.LeaveTypeId == leaveType.LeaveTypeId &&
                        x.Year == year);

                    if (alreadyExists)
                    {
                        skipped++;
                        continue;
                    }

                    decimal carried = 0;

                    // Carry forward unused days from previous year
                    if (carryForward && leaveType.IsCarryForward)
                    {
                        var prevBalance = await _context.LeaveBalances.FirstOrDefaultAsync(x =>
                            x.EmployeeId == employee.EmployeeId &&
                            x.LeaveTypeId == leaveType.LeaveTypeId &&
                            x.Year == year - 1);

                        if (prevBalance != null)
                        {
                            var unused = prevBalance.RemainingDays ?? 0;
                            var maxCarry = leaveType.MaxCarryForwardDays ?? 0;
                            carried = Math.Min(unused, maxCarry);
                            carriedForwardCount++;
                        }
                    }

                    var totalEntitlement = leaveType.AnnualEntitlement + carried;

                    var newBalance = new LeaveBalance
                    {
                        EmployeeId = employee.EmployeeId,
                        LeaveTypeId = leaveType.LeaveTypeId,
                        Year = year,
                        TotalEntitlement = totalEntitlement,
                        UsedDays = 0,
                        CarriedForward = carried,
                        RemainingDays = totalEntitlement,
                        LastUpdated = AppTime.Now
                    };

                    _context.LeaveBalances.Add(newBalance);
                    created++;
                }
            }

            await _context.SaveChangesAsync();

            var result = new GenerateBalanceResultDTO
            {
                Year = year,
                TotalEmployees = activeEmployees.Count,
                TotalLeaveTypes = activeLeaveTypes.Count,
                Created = created,
                Skipped = skipped,
                CarriedForward = carriedForwardCount
            };

            return ServiceResult<GenerateBalanceResultDTO>.Ok("MSG-GEN-01",
                $"Generated {created} leave balance records for year {year}. {skipped} skipped (already exist).",
                result);
        }
    }
}