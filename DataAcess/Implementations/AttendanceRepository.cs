using HRManagement.DataAcess.Interfaces;
using HRManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.DataAcess.Implementations
{
    public class AttendanceRepository : IAttendanceRepository
    {
        private readonly HrmsDbContext _context;
        public AttendanceRepository(HrmsDbContext context)
        {
            _context = context;
        }

        public async Task<AttendanceRecord?> GetAttendanceByEmployeeAndDateAsync(int employeeId, DateOnly attendanceDate)
        {
            return await _context.AttendanceRecords
                .Include(a => a.Employee)
                .Include(a => a.ExplanationLeaveType)
                .Include(a => a.Shift)
                .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.AttendanceDate == attendanceDate);
        }

        public async Task<AttendanceRecord?> GetAttendanceByIdAsync(int attendanceId)
        {
            return await _context.AttendanceRecords
                .Include(a => a.Employee)
                .Include(a => a.ExplanationLeaveType)
                .Include(a => a.Shift)
                .FirstOrDefaultAsync(a => a.AttendanceId == attendanceId);
        }

        public async Task<List<AttendanceRecord>> GetAttendanceByDateAsync(DateOnly date)
        {
            return await _context.AttendanceRecords
                .Include(a => a.Employee)
                .Include(a => a.ExplanationLeaveType)
                .Include(a => a.Shift)
                .Where(a => a.AttendanceDate == date)
                .OrderBy(a => a.Employee.FullName)
                .ToListAsync();
        }

        public async Task<List<AttendanceRecord>> SearchAttendanceAsync(DateOnly? fromDate, DateOnly? toDate, int? employeeId, string? status)
        {
            // Read-only: the search maps these to DTOs, so tracking tens of thousands of rows is wasted work
            var query = _context.AttendanceRecords
                .AsNoTracking()
                .Include(a => a.Employee)
                .Include(a => a.ExplanationLeaveType)
                .Include(a => a.Shift)
                .AsQueryable();

            if (fromDate.HasValue)
                query = query.Where(a => a.AttendanceDate >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(a => a.AttendanceDate <= toDate.Value);

            if (employeeId.HasValue)
                query = query.Where(a => a.EmployeeId == employeeId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(a => a.Status == status);

            // No ORDER BY: the only caller merges these with leave and absence rows and sorts the result itself
            return await query.ToListAsync();
        }

        public async System.Threading.Tasks.Task AddAttendanceAsync(AttendanceRecord attendance)
        {
            await _context.AttendanceRecords.AddAsync(attendance);
        }

        public System.Threading.Tasks.Task UpdateAttendanceAsync(AttendanceRecord attendance)
        {
            _context.AttendanceRecords.Update(attendance);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public async System.Threading.Tasks.Task AddAttendanceLogAsync(AttendanceLog log)
        {
            await _context.AttendanceLogs.AddAsync(log);
        }

        public async Task<List<AttendanceLog>> GetLogsByEmployeeAndDateAsync(int employeeId, DateOnly date)
        {
            var from = date.ToDateTime(TimeOnly.MinValue);
            var to = date.AddDays(1).ToDateTime(TimeOnly.MinValue);

            return await _context.AttendanceLogs
                .Where(l => l.EmployeeId == employeeId && l.LogTime >= from && l.LogTime < to)
                .OrderBy(l => l.LogTime)
                .ToListAsync();
        }

        // FIX: dùng AssignmentDate thay vì StartDate/EndDate
        public async Task<ShiftAssignment?> GetActiveShiftAssignmentAsync(int employeeId, DateOnly date)
        {
            return await _context.ShiftAssignments
                .Include(sa => sa.Shift)
                .FirstOrDefaultAsync(sa =>
                    sa.EmployeeId == employeeId &&
                    sa.Status == "Active" &&
                    sa.AssignmentDate == date);
        }

        public async Task<ShiftAssignment?> GetShiftAssignmentByEmployeeAndDateAsync(int employeeId, DateOnly date)
        {
            return await _context.ShiftAssignments
                .Include(sa => sa.Shift)
                .FirstOrDefaultAsync(sa =>
                    sa.EmployeeId == employeeId &&
                    sa.Status == "Active" &&
                    sa.AssignmentDate == date);
        }

        public async System.Threading.Tasks.Task AddShiftAssignmentAsync(ShiftAssignment assignment)
        {
            await _context.ShiftAssignments.AddAsync(assignment);
        }

        public async Task<Shift?> GetShiftByIdAsync(int shiftId)
        {
            return await _context.Shifts.FirstOrDefaultAsync(s => s.ShiftId == shiftId);
        }

        public async Task<FaceProfile?> GetActiveFaceProfileByEmployeeIdAsync(int employeeId)
        {
            return await _context.FaceProfiles
                .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.Status == "Active");
        }

        public async Task<List<(Employee Employee, FaceProfile? FaceProfile)>> GetAllEmployeesWithFaceProfileAsync()
        {
            var employees = await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.Position)
                .Where(e => e.EmploymentStatus != "Resigned")
                .OrderBy(e => e.FullName)
                .ToListAsync();

            var faceProfiles = await _context.FaceProfiles
                .Where(f => f.Status == "Active")
                .ToListAsync();

            var profileMap = faceProfiles.ToDictionary(f => f.EmployeeId);

            return employees
                .Select(e => (e, profileMap.TryGetValue(e.EmployeeId, out var fp) ? fp : (FaceProfile?)null))
                .ToList();
        }

        public async System.Threading.Tasks.Task AddFaceProfileAsync(FaceProfile faceProfile)
        {
            await _context.FaceProfiles.AddAsync(faceProfile);
        }

        public System.Threading.Tasks.Task UpdateFaceProfileAsync(FaceProfile faceProfile)
        {
            _context.FaceProfiles.Update(faceProfile);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public async System.Threading.Tasks.Task AddFaceVerificationLogAsync(FaceVerificationLog log)
        {
            await _context.FaceVerificationLogs.AddAsync(log);
        }

        public async Task<AttendanceRecord?> GetOpenAttendanceRecordAsync(int employeeId)
        {
            return await _context.AttendanceRecords
                .Include(a => a.Employee)
                .Include(a => a.ExplanationLeaveType)
                .Include(a => a.Shift)
                .Where(a => a.EmployeeId == employeeId
                            && a.CheckInTime.HasValue
                            && !a.CheckOutTime.HasValue)
                .OrderByDescending(a => a.AttendanceDate)
                .ThenByDescending(a => a.CheckInTime)
                .FirstOrDefaultAsync();
        }

        public async Task<AttendanceRecord?> GetLatestAttendanceRecordAsync(int employeeId)
        {
            return await _context.AttendanceRecords
                .Include(a => a.Employee)
                .Include(a => a.ExplanationLeaveType)
                .Include(a => a.Shift)
                .Where(a => a.EmployeeId == employeeId
                            && a.CheckInTime.HasValue)
                .OrderByDescending(a => a.AttendanceDate)
                .ThenByDescending(a => a.CheckInTime)
                .FirstOrDefaultAsync();
        }

        public async System.Threading.Tasks.Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }

    }
}
