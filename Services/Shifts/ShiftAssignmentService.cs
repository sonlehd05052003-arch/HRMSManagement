using HRManagement.DataAcess.Implementations;
using HRManagement.DataAcess.Interfaces;
using HRManagement.DTOs.ShiftAssignments;
using HRManagement.Models;

namespace HRManagement.Services.Shifts
{
    public class ShiftAssignmentService : IShiftAssignmentService
    {
        private readonly IShiftAssignmentRepository _shiftAssignmentRepository;
        private readonly IAttendanceRepository _attendanceRepository;
        private readonly IEmployeeRepository _employeeRepository;
        public ShiftAssignmentService(IShiftAssignmentRepository shiftAssignmentRepository, IAttendanceRepository attendanceRepository, IEmployeeRepository employeeRepository)
        {
            _shiftAssignmentRepository = shiftAssignmentRepository;
            _attendanceRepository = attendanceRepository;
            _employeeRepository = employeeRepository;
        }

        public async System.Threading.Tasks.Task AssignShiftAsync(int managerId, AssignShiftDto dto)
        {
            if (dto.StartDate > dto.EndDate)
                throw new InvalidOperationException("StartDate không được lớn hơn EndDate.");

            var shift = await _attendanceRepository.GetShiftByIdAsync(dto.ShiftId);
            if (shift == null || !shift.IsActive)
                throw new InvalidOperationException("Ca làm việc không hợp lệ hoặc đã bị vô hiệu hóa.");

            var employee = await _employeeRepository.GetEmployeeByIdAsync(dto.EmployeeId);
            if (employee == null)
                throw new KeyNotFoundException("Nhân viên không tồn tại.");

            if (employee.EmploymentStatus != "Active")
                throw new InvalidOperationException("Nhân viên không có trạng thái hoạt động.");

            var today = DateOnly.FromDateTime(AppTime.Today);
            if (employee.JoinDate > today)
                throw new InvalidOperationException("Nhân viên chưa bắt đầu làm việc.");

            var datesToAssign = BuildDatesToAssign(dto);

            var invalidDates = datesToAssign.Where(d => d < employee.JoinDate).ToList();
            if (invalidDates.Any())
            {
                var invalidText = string.Join(", ", invalidDates.Select(d => d.ToString("yyyy-MM-dd")));
                throw new InvalidOperationException($"Không thể phân ca trước ngày vào làm của nhân viên ({employee.JoinDate:yyyy-MM-dd}). Các ngày không hợp lệ: {invalidText}");
            }

            if (!datesToAssign.Any())
                throw new InvalidOperationException("Không có ngày nào hợp lệ để phân ca.");

            var duplicatedDates = new List<DateOnly>();

            foreach (var date in datesToAssign)
            {
                var existing = await _shiftAssignmentRepository.GetShiftAssignmentByEmployeeAndDateAsync(dto.EmployeeId, date);
                if (existing != null && existing.Status == "Active")
                {
                    duplicatedDates.Add(date);
                }
            }

            if (duplicatedDates.Any())
            {
                var duplicateText = string.Join(", ", duplicatedDates.Select(d => d.ToString("yyyy-MM-dd")));
                throw new InvalidOperationException($"Nhân viên đã được phân ca ở các ngày: {duplicateText}");
            }

            foreach (var date in datesToAssign)
            {
                var existing = await _shiftAssignmentRepository.GetShiftAssignmentByEmployeeAndDateAsync(dto.EmployeeId, date);

                if (existing != null && existing.Status != "Active")
                {
                    // Nếu có record cũ nhưng không Active => cập nhật lại thay vì tạo mới
                    existing.ShiftId = dto.ShiftId;
                    existing.StartDate = dto.StartDate;
                    existing.EndDate = dto.EndDate;
                    existing.AssignmentDate = date;
                    existing.RecurrencePattern = dto.AssignType == "Weekly"
                        ? $"Weekly:{string.Join(",", dto.DaysOfWeek ?? new List<int>())}"
                        : "Daily";
                    existing.Status = "Active";

                    await _shiftAssignmentRepository.UpdateShiftAssignmentAsync(existing);
                }
                else if (existing == null)
                {
                    var assignment = new ShiftAssignment
                    {
                        EmployeeId = dto.EmployeeId,
                        ShiftId = dto.ShiftId,
                        AssignmentDate = date,
                        StartDate = dto.StartDate,
                        EndDate = dto.EndDate,
                        RecurrencePattern = dto.AssignType == "Weekly"
                            ? $"Weekly:{string.Join(",", dto.DaysOfWeek ?? new List<int>())}"
                            : "Daily",
                        Status = "Active",
                        CreatedDate = AppTime.Now,
                        CreatedBy = managerId
                    };

                    await _shiftAssignmentRepository.AddShiftAssignmentAsync(assignment);
                }
            }

            await _shiftAssignmentRepository.SaveChangesAsync();
        }

        public async Task<List<ShiftAssignmentResponseDto>> GetShiftAssignmentsAsync(DateOnly? date, int? employeeId, string? status)
        {
            var data = await _shiftAssignmentRepository.GetShiftAssignmentsAsync(date, employeeId, status);
            return data.Select(MapToDto).ToList();
        }

        public async Task<ShiftAssignmentResponseDto?> GetShiftAssignmentByIdAsync(int assignmentId)
        {
            var data = await _shiftAssignmentRepository.GetShiftAssignmentByIdAsync(assignmentId);
            if (data == null) return null;

            return MapToDto(data);
        }

        public async Task<List<ShiftAssignmentResponseDto>> GetMyShiftAssignmentsAsync(int employeeId, DateOnly? fromDate, DateOnly? toDate)
        {
            var data = await _shiftAssignmentRepository.GetMyShiftAssignmentsAsync(employeeId, fromDate, toDate);
            return data.Select(MapToDto).ToList();
        }

        public async Task<ShiftAssignmentResponseDto> UpdateShiftAssignmentAsync(int assignmentId, UpdateShiftAssignmentDto dto)
        {
            var assignment = await _shiftAssignmentRepository.GetShiftAssignmentByIdAsync(assignmentId);
            if (assignment == null)
                throw new KeyNotFoundException("Không tìm thấy phân ca.");

            var shift = await _attendanceRepository.GetShiftByIdAsync(dto.ShiftId);
            if (shift == null || !shift.IsActive)
                throw new InvalidOperationException("Ca làm việc không hợp lệ hoặc đã bị vô hiệu hóa.");

            // Check conflict nếu đổi ngày
            var existing = await _shiftAssignmentRepository.GetShiftAssignmentByEmployeeAndDateAsync(assignment.EmployeeId, dto.AssignmentDate);
            if (existing != null && existing.AssignmentId != assignmentId && existing.Status == "Active")
            {
                throw new InvalidOperationException($"Nhân viên đã có phân ca ở ngày {dto.AssignmentDate:yyyy-MM-dd}.");
            }

            // Nếu ngày này đã có attendance thì không cho sửa (khuyến nghị nghiệp vụ)
            var attendance = await _attendanceRepository.GetAttendanceByEmployeeAndDateAsync(assignment.EmployeeId, assignment.AssignmentDate);
            if (attendance != null)
            {
                throw new InvalidOperationException("Không thể chỉnh sửa phân ca vì ngày này đã phát sinh chấm công.");
            }

            assignment.ShiftId = dto.ShiftId;
            assignment.AssignmentDate = dto.AssignmentDate;
            assignment.Status = dto.Status;

            await _shiftAssignmentRepository.UpdateShiftAssignmentAsync(assignment);
            await _shiftAssignmentRepository.SaveChangesAsync();

            // reload
            var updated = await _shiftAssignmentRepository.GetShiftAssignmentByIdAsync(assignmentId);
            return MapToDto(updated!);
        }

        public async System.Threading.Tasks.Task DeactivateShiftAssignmentAsync(int assignmentId)
        {
            var assignment = await _shiftAssignmentRepository.GetShiftAssignmentByIdAsync(assignmentId);
            if (assignment == null)
                throw new KeyNotFoundException("Không tìm thấy phân ca.");

            var attendance = await _attendanceRepository.GetAttendanceByEmployeeAndDateAsync(assignment.EmployeeId, assignment.AssignmentDate);
            if (attendance != null)
                throw new InvalidOperationException("Không thể hủy phân ca vì ngày này đã phát sinh chấm công.");

            assignment.Status = "Cancelled";

            await _shiftAssignmentRepository.UpdateShiftAssignmentAsync(assignment);
            await _shiftAssignmentRepository.SaveChangesAsync();
        }

        public async System.Threading.Tasks.Task ActivateShiftAssignmentAsync(int assignmentId)
        {
            var assignment = await _shiftAssignmentRepository.GetShiftAssignmentByIdAsync(assignmentId);
            if (assignment == null)
                throw new KeyNotFoundException("Không tìm thấy phân ca.");

            var shift = await _attendanceRepository.GetShiftByIdAsync(assignment.ShiftId);
            if (shift == null || !shift.IsActive)
                throw new InvalidOperationException("Không thể kích hoạt vì ca làm việc đã bị vô hiệu hóa.");

            var existing = await _shiftAssignmentRepository.GetShiftAssignmentByEmployeeAndDateAsync(assignment.EmployeeId, assignment.AssignmentDate);
            if (existing != null && existing.AssignmentId != assignmentId && existing.Status == "Active")
                throw new InvalidOperationException("Nhân viên đã có phân ca active ở ngày này.");

            assignment.Status = "Active";

            await _shiftAssignmentRepository.UpdateShiftAssignmentAsync(assignment);
            await _shiftAssignmentRepository.SaveChangesAsync();
        }

        public async System.Threading.Tasks.Task DeleteShiftAssignmentAsync(int assignmentId)
        {
            var assignment = await _shiftAssignmentRepository.GetShiftAssignmentByIdAsync(assignmentId);
            if (assignment == null)
                throw new KeyNotFoundException("Không tìm thấy phân ca.");

            var attendance = await _attendanceRepository.GetAttendanceByEmployeeAndDateAsync(assignment.EmployeeId, assignment.AssignmentDate);
            if (attendance != null)
                throw new InvalidOperationException("Không thể xóa phân ca vì ngày này đã phát sinh chấm công.");

            await _shiftAssignmentRepository.DeleteShiftAssignmentAsync(assignment);
            await _shiftAssignmentRepository.SaveChangesAsync();
        }

        private List<DateOnly> BuildDatesToAssign(AssignShiftDto dto)
        {
            var datesToAssign = new List<DateOnly>();

            for (var date = dto.StartDate; date <= dto.EndDate; date = date.AddDays(1))
            {
                if (dto.AssignType.Equals("Daily", StringComparison.OrdinalIgnoreCase))
                {
                    datesToAssign.Add(date);
                }
                else if (dto.AssignType.Equals("Weekly", StringComparison.OrdinalIgnoreCase))
                {
                    if (dto.DaysOfWeek == null || !dto.DaysOfWeek.Any())
                        throw new InvalidOperationException("DaysOfWeek bắt buộc khi AssignType = Weekly.");

                    if (dto.DaysOfWeek.Contains((int)date.DayOfWeek))
                    {
                        datesToAssign.Add(date);
                    }
                }
                else
                {
                    throw new InvalidOperationException("AssignType không hợp lệ. Chỉ hỗ trợ 'Daily' hoặc 'Weekly'.");
                }
            }

            return datesToAssign;
        }

        private static ShiftAssignmentResponseDto MapToDto(ShiftAssignment x)
        {
            return new ShiftAssignmentResponseDto
            {
                AssignmentId = x.AssignmentId,
                EmployeeId = x.EmployeeId,
                EmployeeName = x.Employee?.FullName ?? string.Empty,
                ShiftId = x.ShiftId,
                ShiftCode = x.Shift?.ShiftCode ?? string.Empty,
                ShiftName = x.Shift?.ShiftName ?? string.Empty,
                AssignmentDate = x.AssignmentDate,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                RecurrencePattern = x.RecurrencePattern,
                Status = x.Status,
                CreatedDate = x.CreatedDate,
                CreatedBy = x.CreatedBy,
                StartTime = x.Shift?.StartTime,
                EndTime = x.Shift?.EndTime
            };
        }
    }
}
