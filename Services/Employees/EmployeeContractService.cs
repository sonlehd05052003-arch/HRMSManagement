using HRManagement.DTOs;
using HRManagement.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Services.Employees
{
    public static class ContractTypes
    {
        public const string Probation = "Probation";
        public const string FixedTerm = "FixedTerm";
        public const string Indefinite = "Indefinite";

        public static readonly string[] All = { Probation, FixedTerm, Indefinite };
    }

    public static class ContractStatuses
    {
        public const string Active = "Active";
        public const string Terminated = "Terminated";
        /// <summary>Not stored: shown for an active contract whose end date has passed</summary>
        public const string Expired = "Expired";
    }

    public interface IEmployeeContractService
    {
        Task<List<EmployeeContractDto>> GetByEmployeeAsync(int employeeId);
        Task<EmployeeContractDto?> GetByIdAsync(int contractId);
        Task<EmployeeContractDto> CreateAsync(EmployeeContractWriteDto dto, int? createdBy);
        Task<EmployeeContractDto> UpdateAsync(int contractId, EmployeeContractWriteDto dto);
        Task<EmployeeContractDto> TerminateAsync(int contractId, DateOnly endDate);
        /// <summary>Active contracts ending within the next <paramref name="days"/> days, soonest first.</summary>
        Task<List<EmployeeContractDto>> GetExpiringAsync(int days);
    }

    public class EmployeeContractService : IEmployeeContractService
    {
        private readonly HrmsDbContext _context;

        public EmployeeContractService(HrmsDbContext context)
        {
            _context = context;
        }

        public async Task<List<EmployeeContractDto>> GetByEmployeeAsync(int employeeId)
        {
            var contracts = await _context.EmployeeContracts.AsNoTracking()
                .Include(c => c.Employee)
                .Where(c => c.EmployeeId == employeeId)
                .OrderByDescending(c => c.StartDate)
                .ToListAsync();
            return contracts.Select(ToDto).ToList();
        }

        public async Task<EmployeeContractDto?> GetByIdAsync(int contractId)
        {
            var contract = await _context.EmployeeContracts.AsNoTracking()
                .Include(c => c.Employee)
                .FirstOrDefaultAsync(c => c.ContractId == contractId);
            return contract == null ? null : ToDto(contract);
        }

        public async Task<EmployeeContractDto> CreateAsync(EmployeeContractWriteDto dto, int? createdBy)
        {
            if (!await _context.Employees.AnyAsync(e => e.EmployeeId == dto.EmployeeId))
                throw new KeyNotFoundException("Không tìm thấy nhân viên.");

            Validate(dto);
            await EnsureNoOverlapAsync(dto.EmployeeId, dto.StartDate, dto.EndDate, exceptContractId: null);

            var number = string.IsNullOrWhiteSpace(dto.ContractNumber)
                ? await NextNumberAsync(dto.StartDate.Year)
                : dto.ContractNumber.Trim();
            if (await _context.EmployeeContracts.AnyAsync(c => c.ContractNumber == number))
                throw new InvalidOperationException($"Số hợp đồng '{number}' đã tồn tại.");

            var contract = new EmployeeContract
            {
                EmployeeId = dto.EmployeeId,
                ContractNumber = number,
                ContractType = dto.ContractType,
                StartDate = dto.StartDate,
                EndDate = dto.ContractType == ContractTypes.Indefinite ? null : dto.EndDate,
                SalaryAmount = dto.SalaryAmount,
                Terms = dto.Terms?.Trim(),
                Status = ContractStatuses.Active,
                CreatedDate = AppTime.Now,
                CreatedBy = createdBy
            };
            _context.EmployeeContracts.Add(contract);
            await _context.SaveChangesAsync();

            return (await GetByIdAsync(contract.ContractId))!;
        }

        public async Task<EmployeeContractDto> UpdateAsync(int contractId, EmployeeContractWriteDto dto)
        {
            var contract = await _context.EmployeeContracts.FirstOrDefaultAsync(c => c.ContractId == contractId)
                ?? throw new KeyNotFoundException("Không tìm thấy hợp đồng.");
            if (contract.Status == ContractStatuses.Terminated)
                throw new InvalidOperationException("Hợp đồng đã chấm dứt, không thể sửa.");
            if (dto.EmployeeId != contract.EmployeeId)
                throw new ArgumentException("Không thể chuyển hợp đồng sang nhân viên khác.");

            Validate(dto);
            await EnsureNoOverlapAsync(contract.EmployeeId, dto.StartDate, dto.EndDate, exceptContractId: contractId);

            if (!string.IsNullOrWhiteSpace(dto.ContractNumber) && dto.ContractNumber.Trim() != contract.ContractNumber)
            {
                var number = dto.ContractNumber.Trim();
                if (await _context.EmployeeContracts.AnyAsync(c => c.ContractNumber == number && c.ContractId != contractId))
                    throw new InvalidOperationException($"Số hợp đồng '{number}' đã tồn tại.");
                contract.ContractNumber = number;
            }

            contract.ContractType = dto.ContractType;
            contract.StartDate = dto.StartDate;
            contract.EndDate = dto.ContractType == ContractTypes.Indefinite ? null : dto.EndDate;
            contract.SalaryAmount = dto.SalaryAmount;
            contract.Terms = dto.Terms?.Trim();
            await _context.SaveChangesAsync();

            return (await GetByIdAsync(contractId))!;
        }

        public async Task<EmployeeContractDto> TerminateAsync(int contractId, DateOnly endDate)
        {
            var contract = await _context.EmployeeContracts.FirstOrDefaultAsync(c => c.ContractId == contractId)
                ?? throw new KeyNotFoundException("Không tìm thấy hợp đồng.");
            if (contract.Status == ContractStatuses.Terminated)
                throw new InvalidOperationException("Hợp đồng đã chấm dứt trước đó.");
            if (endDate < contract.StartDate)
                throw new ArgumentException("Ngày chấm dứt không được trước ngày bắt đầu hợp đồng.");

            contract.Status = ContractStatuses.Terminated;
            contract.EndDate = endDate;
            await _context.SaveChangesAsync();

            return (await GetByIdAsync(contractId))!;
        }

        public async Task<List<EmployeeContractDto>> GetExpiringAsync(int days)
        {
            var today = DateOnly.FromDateTime(AppTime.Today);
            var until = today.AddDays(days);
            var contracts = await _context.EmployeeContracts.AsNoTracking()
                .Include(c => c.Employee)
                .Where(c => c.Status == ContractStatuses.Active && c.EndDate != null && c.EndDate >= today && c.EndDate <= until)
                .OrderBy(c => c.EndDate)
                .ToListAsync();
            return contracts.Select(ToDto).ToList();
        }

        private static void Validate(EmployeeContractWriteDto dto)
        {
            if (!ContractTypes.All.Contains(dto.ContractType))
                throw new ArgumentException("Loại hợp đồng không hợp lệ.");
            if (dto.ContractType != ContractTypes.Indefinite && dto.EndDate == null)
                throw new ArgumentException("Hợp đồng thử việc và hợp đồng xác định thời hạn phải có ngày kết thúc.");
            if (dto.EndDate.HasValue && dto.ContractType != ContractTypes.Indefinite && dto.EndDate < dto.StartDate)
                throw new ArgumentException("Ngày kết thúc phải sau ngày bắt đầu.");
            if (dto.SalaryAmount < 0)
                throw new ArgumentException("Mức lương không hợp lệ.");
        }

        // One person has at most one active contract on any given day
        private async Task EnsureNoOverlapAsync(int employeeId, DateOnly start, DateOnly? end, int? exceptContractId)
        {
            var others = await _context.EmployeeContracts.AsNoTracking()
                .Where(c => c.EmployeeId == employeeId && c.Status == ContractStatuses.Active && c.ContractId != exceptContractId)
                .Select(c => new { c.ContractNumber, c.StartDate, c.EndDate })
                .ToListAsync();

            var clash = others.FirstOrDefault(c =>
                (end == null || c.StartDate <= end) && (c.EndDate == null || c.EndDate >= start));
            if (clash != null)
                throw new InvalidOperationException(
                    $"Trùng thời gian với hợp đồng {clash.ContractNumber} đang hiệu lực. Hãy chấm dứt hoặc sửa hợp đồng đó trước.");
        }

        private async Task<string> NextNumberAsync(int year)
        {
            var prefix = $"HD-{year}-";
            var numbers = await _context.EmployeeContracts
                .Where(c => c.ContractNumber.StartsWith(prefix))
                .Select(c => c.ContractNumber)
                .ToListAsync();
            var next = numbers.Select(n => int.TryParse(n[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max() + 1;
            return $"{prefix}{next:0000}";
        }

        private static EmployeeContractDto ToDto(EmployeeContract c)
        {
            var today = DateOnly.FromDateTime(AppTime.Today);
            var expired = c.Status == ContractStatuses.Active && c.EndDate.HasValue && c.EndDate < today;
            return new EmployeeContractDto
            {
                ContractId = c.ContractId,
                EmployeeId = c.EmployeeId,
                EmployeeName = c.Employee?.FullName ?? string.Empty,
                ContractNumber = c.ContractNumber,
                ContractType = c.ContractType,
                StartDate = c.StartDate,
                EndDate = c.EndDate,
                SalaryAmount = c.SalaryAmount,
                Terms = c.Terms,
                Status = expired ? ContractStatuses.Expired : c.Status,
                DaysRemaining = c.Status == ContractStatuses.Active && c.EndDate.HasValue ? c.EndDate.Value.DayNumber - today.DayNumber : null,
                CreatedDate = c.CreatedDate
            };
        }
    }
}
