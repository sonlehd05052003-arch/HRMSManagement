using HRManagement.DTOs;
using HRManagement.DTOs.Attendances;

namespace HRManagement.Services.Attendances
{
    public interface IAttendanceService
    {
        Task<AttendanceResponseDto> CheckInAsync(int employeeId, CheckInRequestDto dto);
        Task<AttendanceResponseDto> CheckOutAsync(int employeeId, CheckOutRequestDto dto);

        Task<AttendanceDetailResponseDto?> GetMyTodayAsync(int employeeId);
        Task<List<AttendanceResponseDto>> GetMyHistoryAsync(int employeeId, DateOnly? fromDate, DateOnly? toDate);

        Task<List<AttendanceResponseDto>> GetAttendanceByDateAsync(DateOnly date);
        Task<List<AttendanceResponseDto>> SearchAttendanceAsync(DateOnly? fromDate, DateOnly? toDate, int? employeeId, string? status);
        /// <summary>
        /// One page of the search. <paramref name="managerEmployeeId"/> limits it to that manager's direct reports.
        /// </summary>
        Task<PagedResultDto<AttendanceResponseDto>> SearchAttendancePageAsync(
            DateOnly? fromDate, DateOnly? toDate, int? employeeId, string? status, string? explanationStatus,
            int? managerEmployeeId, int page, int pageSize);
        Task<AttendanceDetailResponseDto?> GetAttendanceDetailAsync(int employeeId, DateOnly date);

        Task<AttendanceResponseDto> ManualAdjustAttendanceAsync(int attendanceId, int approverId, ManualAdjustAttendanceDto dto);
        Task<AttendanceResponseDto> ManualCreateAttendanceAsync(int approverId, ManualCreateAttendanceDto dto);

        Task LockAttendanceAsync(int attendanceId, int userId);
        Task UnlockAttendanceAsync(int attendanceId, int userId);

        Task<List<AttendanceLogResponseDto>> GetLogsAsync(int employeeId, DateOnly date);
        Task<AttendanceResponseDto> SubmitExplanationAsync(int employeeId, int attendanceId, SubmitExplanationDto dto);
        Task<AttendanceResponseDto> SubmitAbsentExplanationAsync(int employeeId, DateOnly date, SubmitAbsentExplanationDto dto);
        Task<AttendanceResponseDto> ApproveExplanationAsync(int managerId, int attendanceId, ApproveExplanationDto dto);
    }
}
