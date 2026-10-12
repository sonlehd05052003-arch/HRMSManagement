using HRManagement.DTOs.LeaveTypes;
using HRManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Services.Leaves
{
    public class LeaveTypeService : ILeaveTypeService
    {
        private readonly HrmsDbContext _context;

        public LeaveTypeService(HrmsDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<LeaveTypeDTO>> GetActiveLeaveTypesAsync()
        {
            return await _context.LeaveTypes
                .Where(lt => lt.IsActive)
                .Select(lt => new LeaveTypeDTO
                {
                    LeaveTypeId = lt.LeaveTypeId,
                    LeaveTypeCode = lt.LeaveTypeCode,
                    LeaveTypeName = lt.LeaveTypeName,
                    AnnualEntitlement = lt.AnnualEntitlement,
                    IsPaid = lt.IsPaid,
                    RequiresApproval = lt.RequiresApproval,
                    IsCarryForward = lt.IsCarryForward,
                    MaxCarryForwardDays = lt.MaxCarryForwardDays,
                    IsActive = lt.IsActive
                })
                .ToListAsync();
        }

        public async Task<IEnumerable<LeaveTypeDTO>> GetAllLeaveTypesAsync()
        {
            return await _context.LeaveTypes
                .Select(lt => new LeaveTypeDTO
                {
                    LeaveTypeId = lt.LeaveTypeId,
                    LeaveTypeCode = lt.LeaveTypeCode,
                    LeaveTypeName = lt.LeaveTypeName,
                    AnnualEntitlement = lt.AnnualEntitlement,
                    IsPaid = lt.IsPaid,
                    RequiresApproval = lt.RequiresApproval,
                    IsCarryForward = lt.IsCarryForward,
                    MaxCarryForwardDays = lt.MaxCarryForwardDays,
                    IsActive = lt.IsActive
                })
                .ToListAsync();
        }

        public async Task<LeaveTypeDTO?> GetLeaveTypeByIdAsync(int id)
        {
            return await _context.LeaveTypes
                .Where(lt => lt.LeaveTypeId == id)
                .Select(lt => new LeaveTypeDTO
                {
                    LeaveTypeId = lt.LeaveTypeId,
                    LeaveTypeCode = lt.LeaveTypeCode,
                    LeaveTypeName = lt.LeaveTypeName,
                    AnnualEntitlement = lt.AnnualEntitlement,
                    IsPaid = lt.IsPaid,
                    RequiresApproval = lt.RequiresApproval,
                    IsCarryForward = lt.IsCarryForward,
                    MaxCarryForwardDays = lt.MaxCarryForwardDays,
                    IsActive = lt.IsActive
                })
                .FirstOrDefaultAsync();
        }

        public async Task<LeaveTypeDTO> CreateLeaveTypeAsync(CreateLeaveTypeDTO dto)
        {
            // Auto-generate code if empty or provided
            string finalCode = string.IsNullOrWhiteSpace(dto.LeaveTypeCode) 
                ? await GenerateUniqueCodeAsync(dto.LeaveTypeName)
                : dto.LeaveTypeCode.Trim().ToUpper();

            // Check duplicate code for provided code
            if (!string.IsNullOrWhiteSpace(dto.LeaveTypeCode))
            {
                bool codeExists = await _context.LeaveTypes
                    .AnyAsync(x => x.LeaveTypeCode.ToLower() == finalCode.ToLower());
                if (codeExists)
                    finalCode = await GenerateUniqueCodeAsync(dto.LeaveTypeName); // Fallback to auto-gen if provided exists
            }

            // Nếu IsCarryForward = false thì MaxCarryForwardDays nên = null
            if (!dto.IsCarryForward)
            {
                dto.MaxCarryForwardDays = null;
            }

            var leaveType = new LeaveType
            {
                LeaveTypeCode = finalCode,
                LeaveTypeName = dto.LeaveTypeName.Trim(),
                AnnualEntitlement = dto.AnnualEntitlement,
                IsPaid = dto.IsPaid,
                RequiresApproval = dto.RequiresApproval,
                IsCarryForward = dto.IsCarryForward,
                MaxCarryForwardDays = dto.MaxCarryForwardDays,
                IsActive = dto.IsActive,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = null
            };

            _context.LeaveTypes.Add(leaveType);
            await _context.SaveChangesAsync();
            return new LeaveTypeDTO
            {
                LeaveTypeId = leaveType.LeaveTypeId,
                LeaveTypeCode = leaveType.LeaveTypeCode,
                LeaveTypeName = leaveType.LeaveTypeName,
                AnnualEntitlement = leaveType.AnnualEntitlement,
                IsPaid = leaveType.IsPaid,
                RequiresApproval = leaveType.RequiresApproval,
                IsCarryForward = leaveType.IsCarryForward,
                MaxCarryForwardDays = leaveType.MaxCarryForwardDays,
                IsActive = leaveType.IsActive
            };
        }

        private async Task<string> GenerateUniqueCodeAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "LT";

            // Lấy các chữ cái đầu của mỗi từ, ví dụ: "Sick Leave" -> "SL"
            var words = name.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var baseCode = string.Join("", words.Select(w => char.ToUpper(w[0])));

            if (baseCode.Length < 2 && name.Length >= 2) 
                baseCode = name.Substring(0, 2).ToUpper();

            string code = baseCode;
            int counter = 1;

            while (await _context.LeaveTypes.AnyAsync(x => x.LeaveTypeCode == code))
            {
                code = $"{baseCode}{counter++}";
            }

            return code;
        }

        public async Task<LeaveTypeDTO?> UpdateLeaveTypeAsync(int id, UpdateLeaveTypeDTO dto)
        {
            var leaveType = await _context.LeaveTypes.FindAsync(id);
            if (leaveType == null)
                return null;

            // Check duplicate code (trừ chính nó)
            bool codeExists = await _context.LeaveTypes
                .AnyAsync(x => x.LeaveTypeId != id &&
                               x.LeaveTypeCode.ToLower() == dto.LeaveTypeCode.ToLower());

            if (codeExists)
                throw new InvalidOperationException("LeaveTypeCode already exists.");

            leaveType.LeaveTypeCode = dto.LeaveTypeCode.Trim();
            leaveType.LeaveTypeName = dto.LeaveTypeName.Trim();
            leaveType.AnnualEntitlement = dto.AnnualEntitlement;
            leaveType.IsPaid = dto.IsPaid;
            leaveType.RequiresApproval = dto.RequiresApproval;
            leaveType.IsCarryForward = dto.IsCarryForward;
            leaveType.MaxCarryForwardDays = dto.IsCarryForward ? dto.MaxCarryForwardDays : null;
            leaveType.IsActive = dto.IsActive;

            await _context.SaveChangesAsync();

            return new LeaveTypeDTO
            {
                LeaveTypeId = leaveType.LeaveTypeId,
                LeaveTypeCode = leaveType.LeaveTypeCode,
                LeaveTypeName = leaveType.LeaveTypeName,
                AnnualEntitlement = leaveType.AnnualEntitlement,
                IsPaid = leaveType.IsPaid,
                RequiresApproval = leaveType.RequiresApproval,
                IsCarryForward = leaveType.IsCarryForward,
                MaxCarryForwardDays = leaveType.MaxCarryForwardDays,
                IsActive = leaveType.IsActive
            };
        }

        public async Task<bool> SoftDeleteLeaveTypeAsync(int id)
        {
            var leaveType = await _context.LeaveTypes.FindAsync(id);
            if (leaveType == null)
                return false;

            leaveType.IsActive = false;
            await _context.SaveChangesAsync();

            return true;
        }
    }
}