using HRManagement.DataAcess.Interfaces;
using HRManagement.DTOs.Shifts;
using HRManagement.Models;

namespace HRManagement.Services.Shifts
{
    public class ShiftService : IShiftService
    {
        private readonly IShiftRepository _shiftRepository;
    
            public ShiftService(IShiftRepository shiftRepository)
            {
                _shiftRepository = shiftRepository;
            }

        public async Task<ShiftResponseDto> CreateShiftAsync(int currentUserId, CreateShiftDto dto)
        {
            var existing = await _shiftRepository.GetShiftByCodeAsync(dto.ShiftCode);
            if (existing != null)
                throw new InvalidOperationException($"Shift code '{dto.ShiftCode}' đã tồn tại.");

            var shift = new Shift
            {
                ShiftCode = dto.ShiftCode,
                ShiftName = dto.ShiftName,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                WorkingHours = dto.WorkingHours,
                ShiftType = dto.ShiftType,
                LateGraceMinutes = dto.LateGraceMinutes,
                EarlyCheckInMinutes = dto.EarlyCheckInMinutes,
                LatestCheckInMinutes = dto.LatestCheckInMinutes,
                EarliestCheckOutMinutes = dto.EarliestCheckOutMinutes,
                LatestCheckOutMinutes = dto.LatestCheckOutMinutes,
                IsOvernight = dto.IsOvernight,
                IsActive = true,
                CreatedDate = AppTime.Now,
                CreatedBy = currentUserId
            };

            await _shiftRepository.AddShiftAsync(shift);
            await _shiftRepository.SaveChangesAsync();

            return MapToDto(shift);
        }

        public async Task<List<ShiftResponseDto>> GetAllShiftsAsync(bool? isActive)
        {
            var shifts = await _shiftRepository.GetAllShiftsAsync(isActive);
            return shifts.Select(MapToDto).ToList();
        }

        public async Task<ShiftResponseDto?> GetShiftByIdAsync(int shiftId)
        {
            var shift = await _shiftRepository.GetShiftByIdAsync(shiftId);
            if (shift == null) return null;

            return MapToDto(shift);
        }

        public async Task<ShiftResponseDto> UpdateShiftAsync(int shiftId, UpdateShiftDto dto)
        {
            var shift = await _shiftRepository.GetShiftByIdAsync(shiftId);
            if (shift == null)
                throw new KeyNotFoundException("Không tìm thấy ca làm việc.");

            var existing = await _shiftRepository.GetShiftByCodeAsync(dto.ShiftCode);
            if (existing != null && existing.ShiftId != shiftId)
                throw new InvalidOperationException($"Shift code '{dto.ShiftCode}' đã tồn tại.");

            shift.ShiftCode = dto.ShiftCode;
            shift.ShiftName = dto.ShiftName;
            shift.StartTime = dto.StartTime;
            shift.EndTime = dto.EndTime;
            shift.WorkingHours = dto.WorkingHours;
            shift.ShiftType = dto.ShiftType;
            shift.LateGraceMinutes = dto.LateGraceMinutes;
            shift.EarlyCheckInMinutes = dto.EarlyCheckInMinutes;
            shift.LatestCheckInMinutes = dto.LatestCheckInMinutes;
            shift.EarliestCheckOutMinutes = dto.EarliestCheckOutMinutes;
            shift.LatestCheckOutMinutes = dto.LatestCheckOutMinutes;
            shift.IsOvernight = dto.IsOvernight;
            shift.IsActive = dto.IsActive;

            await _shiftRepository.UpdateShiftAsync(shift);
            await _shiftRepository.SaveChangesAsync();

            return MapToDto(shift);
        }

        public async System.Threading.Tasks.Task DeactivateShiftAsync(int shiftId)
        {
            var shift = await _shiftRepository.GetShiftByIdAsync(shiftId);
            if (shift == null)
                throw new KeyNotFoundException("Không tìm thấy ca làm việc.");

            if (!shift.IsActive)
                return;

            var hasActiveOrFutureAssignments = await _shiftRepository.HasActiveOrFutureAssignmentsAsync(shiftId);

            if (hasActiveOrFutureAssignments)
                throw new InvalidOperationException("Không thể ngừng sử dụng ca làm việc vì vẫn đang có nhân viên được phân ca này ở hiện tại hoặc tương lai.");

            shift.IsActive = false;

            await _shiftRepository.UpdateShiftAsync(shift);
            await _shiftRepository.SaveChangesAsync();
        }


        public async System.Threading.Tasks.Task ActivateShiftAsync(int shiftId)
        {
            var shift = await _shiftRepository.GetShiftByIdAsync(shiftId);
            if (shift == null)
                throw new KeyNotFoundException("Không tìm thấy ca làm việc.");

            if (shift.IsActive)
                return;

            shift.IsActive = true;

            await _shiftRepository.UpdateShiftAsync(shift);
            await _shiftRepository.SaveChangesAsync();
        }

        private static ShiftResponseDto MapToDto(Shift shift)
        {
            return new ShiftResponseDto
            {
                ShiftId = shift.ShiftId,
                ShiftCode = shift.ShiftCode,
                ShiftName = shift.ShiftName,
                StartTime = shift.StartTime,
                EndTime = shift.EndTime,
                WorkingHours = shift.WorkingHours,
                ShiftType = shift.ShiftType,
                LateGraceMinutes = shift.LateGraceMinutes,
                EarlyCheckInMinutes = shift.EarlyCheckInMinutes,
                LatestCheckInMinutes = shift.LatestCheckInMinutes,
                EarliestCheckOutMinutes = shift.EarliestCheckOutMinutes,
                LatestCheckOutMinutes = shift.LatestCheckOutMinutes,
                IsOvernight = shift.IsOvernight,
                IsActive = shift.IsActive,
                CreatedDate = shift.CreatedDate,
                CreatedBy = shift.CreatedBy
            };
        }
    }
}
