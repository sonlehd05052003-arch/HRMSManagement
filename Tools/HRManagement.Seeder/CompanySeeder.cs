using HRManagement.DTOs;
using HRManagement.Models;
using HRManagement.Services.Companies;
using HRManagement.Services.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Seeder
{
    public sealed record SeedOptions(int Companies, int EmployeesPerCompany, int Days, string Password);

    public sealed record CompanyStats(string Code, int Employees, int Attendance, int ShiftAssignments, int LeaveRequests, int OvertimeRequests);

    /// <summary>
    /// Creates one generated company through CompanyService (so it gets the same roles, menus, leave types, shifts
    /// and payroll settings as a real one), then fills it with departments, positions, employees with accounts,
    /// leave balances and requests, overtime requests, shift assignments and attendance for the past days.
    /// Every code starts with "PERF" so the whole set can be found and removed again.
    /// </summary>
    public class CompanySeeder
    {
        public const string CodePrefix = "PERF";
        private const string Domain = "perf.test";

        private readonly IServiceProvider _services;
        private readonly SeedOptions _options;
        private readonly string _passwordHash;

        public CompanySeeder(IServiceProvider services, SeedOptions options, string passwordHash)
        {
            _services = services;
            _options = options;
            _passwordHash = passwordHash;
        }

        private sealed class Person
        {
            public Employee Employee = null!;
            public User User = null!;
            public Person? Manager;
            public HashSet<DateOnly> LeaveDays = new();
        }

        public async Task<CompanyStats> SeedAsync(int number)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HrmsDbContext>();
            var companyService = scope.ServiceProvider.GetRequiredService<ICompanyService>();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            var rng = new Random(number * 7919);
            var code = $"{CodePrefix}{number:000}";
            var slug = code.ToLowerInvariant();

            var (created, error) = await companyService.CreateAsync(new CreateCompanyDTO
            {
                CompanyCode = code,
                CompanyName = $"Công ty Kiểm thử {number:000}",
                TaxCode = $"0{100000000 + number}",
                Email = $"contact@{slug}.{Domain}",
                Phone = $"028{rng.Next(10_000_000, 99_999_999)}",
                Address = $"{rng.Next(1, 500)} Đường Số {rng.Next(1, 50)}, {FakeData.Cities[rng.Next(FakeData.Cities.Length)]}",
                Admin = new CreateCompanyAdminDTO
                {
                    FirstName = "Quản trị",
                    LastName = code,
                    Username = $"{slug}.admin",
                    Email = $"admin@{slug}.{Domain}"
                }
            });
            if (created == null)
                throw new InvalidOperationException($"{code}: {error}");

            tenant.Use(created.CompanyId);
            db.ChangeTracker.Clear();

            var today = DateOnly.FromDateTime(AppTime.Today);
            var now = AppTime.Now;

            var roles = await db.Roles.ToDictionaryAsync(r => r.RoleName);
            var shift = await db.Shifts.OrderBy(s => s.ShiftId).FirstAsync();
            var leaveTypes = await db.LeaveTypes.Where(t => t.IsActive).OrderBy(t => t.LeaveTypeId).ToListAsync();

            // ── Organisation ──────────────────────────────────────────────
            var departmentCount = Math.Clamp(_options.EmployeesPerCompany / 15, 1, FakeData.Departments.Length);
            var departments = FakeData.Departments.Take(departmentCount)
                .Select(d => new Department { DepartmentCode = d.Code, DepartmentName = d.Name, IsActive = true, CreatedDate = now })
                .ToList();
            var positions = FakeData.Positions
                .Select(p => new Position { PositionCode = p.Code, PositionName = p.Name, Level = p.Level, IsTopLevel = p.IsTopLevel, IsActive = true, CreatedDate = now })
                .ToList();
            db.Departments.AddRange(departments);
            db.Positions.AddRange(positions);
            await db.SaveChangesAsync();

            // The admin CompanyService created becomes the director everyone reports to
            var adminUser = await db.Users.Include(u => u.Employee).SingleAsync(u => u.Username == created.AdminUsername);
            adminUser.PasswordHash = _passwordHash;
            adminUser.Employee!.PositionId = positions[0].PositionId;
            adminUser.Employee.JoinDate = today.AddYears(-6);
            var director = new Person { Employee = adminUser.Employee, User = adminUser };

            // ── Employees and their accounts ──────────────────────────────
            var people = new List<Person>();
            for (var i = 0; i < _options.EmployeesPerCompany - 1; i++)
            {
                var department = departments[i % departmentCount];
                var positionIndex = i < departmentCount ? 1 : i < departmentCount * 2 ? 2 : 3 + rng.Next(3);
                var employeeCode = $"EMP{i + 2:0000}";
                var email = $"{employeeCode.ToLowerInvariant()}@{slug}.{Domain}";

                var employee = NewEmployee(rng, employeeCode, email, department.DepartmentId, positions[positionIndex].PositionId, positionIndex, today, now);
                var role = positionIndex == 1 ? "MANAGE" : department.DepartmentCode == "NS" ? "HR" : "EMPLOYEE";
                var user = new User
                {
                    Username = $"{slug}.{employeeCode.ToLowerInvariant()}",
                    Email = email,
                    PasswordHash = _passwordHash,
                    Employee = employee,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                };
                user.UserRoles.Add(new UserRole { RoleId = roles[role].RoleId });

                people.Add(new Person { Employee = employee, User = user });
            }

            db.Users.AddRange(people.Select(p => p.User));
            await db.SaveChangesAsync();

            // Department heads report to the director, everyone else to the head of their department
            var heads = people.Take(departmentCount).ToList();
            for (var d = 0; d < departmentCount; d++)
            {
                departments[d].ManagerId = heads[d].Employee.EmployeeId;
                heads[d].Manager = director;
            }
            foreach (var person in people.Skip(departmentCount))
                person.Manager = heads.First(h => h.Employee.DepartmentId == person.Employee.DepartmentId);
            foreach (var person in people)
                person.Employee.ManagerId = person.Manager!.Employee.EmployeeId;
            await db.SaveChangesAsync();

            var everyone = people.Prepend(director).ToList();

            // ── Leave requests and balances ───────────────────────────────
            var windowStart = today.AddDays(-_options.Days);
            var leaveRequests = new List<LeaveRequest>();
            var usedDays = new Dictionary<(int EmployeeId, int LeaveTypeId), decimal>();
            var requestSeq = 0;

            foreach (var person in people)
            {
                var start = NextWeekday(windowStart.AddDays(rng.Next(0, 15)));
                for (var n = rng.Next(0, 3); n > 0; n--)
                {
                    var days = WeekdaysFrom(start, rng.Next(1, 4));
                    var end = days[^1];
                    if (end > today.AddDays(30))
                        break;

                    var leaveType = PickLeaveType(rng, leaveTypes);
                    var submitted = start.AddDays(-rng.Next(2, 10)).ToDateTime(new TimeOnly(9, rng.Next(0, 60)));
                    var approver = person.Manager!.User.UserId;
                    var status = start < today
                        ? (rng.NextDouble() < 0.85 ? "Approved" : "Rejected")
                        : (rng.NextDouble() < 0.7 ? "Pending" : "Approved");

                    var request = new LeaveRequest
                    {
                        RequestNumber = $"LR-{submitted:yyyyMMdd}-{++requestSeq:00000}",
                        EmployeeId = person.Employee.EmployeeId,
                        LeaveTypeId = leaveType.LeaveTypeId,
                        StartDate = start,
                        EndDate = end,
                        NumberOfDays = days.Count,
                        Reason = FakeData.LeaveReasons[rng.Next(FakeData.LeaveReasons.Length)],
                        Status = status,
                        SubmittedDate = submitted,
                        TargetApproverId = approver
                    };
                    if (status != "Pending")
                    {
                        request.ReviewedBy = approver;
                        request.ReviewedDate = submitted.AddDays(1);
                    }
                    if (status == "Approved")
                    {
                        request.ApprovedBy = approver;
                        request.ApprovedDate = submitted.AddDays(1);
                        person.LeaveDays.UnionWith(days);
                        if (start.Year == today.Year)
                        {
                            var key = (person.Employee.EmployeeId, leaveType.LeaveTypeId);
                            usedDays[key] = usedDays.GetValueOrDefault(key) + days.Count;
                        }
                    }
                    else if (status == "Rejected")
                    {
                        request.RejectionReason = "Trùng lịch công việc quan trọng của phòng.";
                    }
                    leaveRequests.Add(request);

                    start = NextWeekday(end.AddDays(rng.Next(7, 25)));
                }
            }
            db.LeaveRequests.AddRange(leaveRequests);

            var balances = everyone
                .SelectMany(p => leaveTypes.Where(t => t.AnnualEntitlement > 0).Select(t => new LeaveBalance
                {
                    EmployeeId = p.Employee.EmployeeId,
                    LeaveTypeId = t.LeaveTypeId,
                    Year = today.Year,
                    TotalEntitlement = t.AnnualEntitlement,
                    UsedDays = usedDays.GetValueOrDefault((p.Employee.EmployeeId, t.LeaveTypeId)),
                    CarriedForward = 0,
                    LastUpdated = now
                }))
                .ToList();
            db.LeaveBalances.AddRange(balances);

            // ── Overtime requests ─────────────────────────────────────────
            var overtime = new List<OvertimeRequest>();
            var overtimeSeq = 0;
            foreach (var person in people.Where(_ => rng.NextDouble() < 0.3))
            {
                var date = NextWeekday(today.AddDays(-rng.Next(1, Math.Max(2, Math.Min(_options.Days, 30)))));
                if (date >= today || person.LeaveDays.Contains(date))
                    continue;

                var approver = person.Manager!.User.UserId;
                var submitted = date.ToDateTime(new TimeOnly(10, 0));
                var approved = rng.NextDouble() < 0.7;
                overtime.Add(new OvertimeRequest
                {
                    RequestNumber = $"OT-{date:yyyyMMdd}-{++overtimeSeq:00000}",
                    EmployeeId = person.Employee.EmployeeId,
                    OvertimeDate = date,
                    StartTime = new TimeOnly(18, 0),
                    EndTime = new TimeOnly(20, 0),
                    TotalHours = 2,
                    Reason = "Đẩy nhanh tiến độ",
                    TaskDescription = FakeData.OvertimeTasks[rng.Next(FakeData.OvertimeTasks.Length)],
                    Status = approved ? "Approved" : "Pending",
                    SubmittedDate = submitted,
                    TargetApproverId = approver,
                    ReviewedBy = approved ? approver : null,
                    ReviewedDate = approved ? submitted.AddHours(3) : null,
                    ApprovedBy = approved ? approver : null,
                    ApprovedDate = approved ? submitted.AddHours(3) : null
                });
            }
            db.OvertimeRequests.AddRange(overtime);
            await db.SaveChangesAsync();

            // ── Shift assignments and attendance (bulk) ───────────────────
            var assignmentEnd = today.AddDays(14);
            var assignments = new List<ShiftAssignment>();
            var attendance = new List<AttendanceRecord>();

            foreach (var person in everyone)
            {
                var employee = person.Employee;
                var first = employee.JoinDate > windowStart ? employee.JoinDate : windowStart;
                for (var day = first; day <= assignmentEnd; day = day.AddDays(1))
                {
                    if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                        continue;

                    assignments.Add(new ShiftAssignment
                    {
                        CompanyId = created.CompanyId,
                        EmployeeId = employee.EmployeeId,
                        ShiftId = shift.ShiftId,
                        AssignmentDate = day,
                        StartDate = first,
                        EndDate = assignmentEnd,
                        RecurrencePattern = "Daily",
                        Status = "Active",
                        CreatedDate = now,
                        CreatedBy = director.User.UserId
                    });

                    if (day < today && !person.LeaveDays.Contains(day))
                        attendance.Add(NewAttendance(rng, created.CompanyId, employee.EmployeeId, shift, day));
                }
            }

            await BulkInserter.InsertAsync(db, assignments);
            await BulkInserter.InsertAsync(db, attendance);

            return new CompanyStats(code, everyone.Count, attendance.Count, assignments.Count, leaveRequests.Count, overtime.Count);
        }

        private static Employee NewEmployee(Random rng, string code, string email, int departmentId, int positionId,
            int positionIndex, DateOnly today, DateTime now)
        {
            var female = rng.NextDouble() < 0.5;
            var family = FakeData.FamilyNames[rng.Next(FakeData.FamilyNames.Length)];
            var middle = female
                ? FakeData.FemaleMiddleNames[rng.Next(FakeData.FemaleMiddleNames.Length)]
                : FakeData.MaleMiddleNames[rng.Next(FakeData.MaleMiddleNames.Length)];
            var given = female
                ? FakeData.FemaleGivenNames[rng.Next(FakeData.FemaleGivenNames.Length)]
                : FakeData.MaleGivenNames[rng.Next(FakeData.MaleGivenNames.Length)];

            // Monthly salary band per position: head, deputy, specialist, staff, intern
            var (min, max) = positionIndex switch
            {
                1 => (35, 60), 2 => (25, 40), 3 => (15, 30), 4 => (9, 18), _ => (4, 7)
            };
            var salary = rng.Next(min * 10, max * 10 + 1) * 100_000m;

            return new Employee
            {
                EmployeeCode = code,
                // As in the employee form: FirstName holds family + middle name, LastName the given name
                FirstName = $"{family} {middle}",
                LastName = given,
                Email = email,
                Phone = $"09{rng.Next(10_000_000, 99_999_999)}",
                Gender = female ? "Female" : "Male",
                DateOfBirth = today.AddYears(-rng.Next(22, 56)).AddDays(-rng.Next(0, 365)),
                City = FakeData.Cities[rng.Next(FakeData.Cities.Length)],
                Country = "Việt Nam",
                DepartmentId = departmentId,
                PositionId = positionId,
                JoinDate = today.AddDays(-rng.Next(20, 2500)),
                EmploymentStatus = "Active",
                EmploymentType = "Full-Time",
                BaseSalary = salary,
                InsuranceSalary = salary,
                NumberOfDependents = rng.NextDouble() < 0.6 ? 0 : rng.Next(1, 4),
                CreatedDate = now
            };
        }

        private static AttendanceRecord NewAttendance(Random rng, int companyId, int employeeId, Shift shift, DateOnly day)
        {
            var record = new AttendanceRecord
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                AttendanceDate = day,
                ShiftId = shift.ShiftId,
                OvertimeHours = 0,
                LateMinutes = 0,
                EarlyLeaveMinutes = 0,
                IsManualAdjusted = false,
                IsLocked = false
            };

            var roll = rng.NextDouble();
            if (roll < 0.03)
            {
                record.Status = "Absent";
                record.Source = "System";
                record.CreatedDate = day.ToDateTime(new TimeOnly(23, 0));
                return record;
            }

            var shiftStart = day.ToDateTime(shift.StartTime);
            var shiftEnd = day.ToDateTime(shift.EndTime);
            var late = roll < 0.11;
            var checkIn = late ? shiftStart.AddMinutes(rng.Next(6, 50)) : shiftStart.AddMinutes(-rng.Next(0, 25));
            var earlyLeave = rng.NextDouble() < 0.02;
            var checkOut = earlyLeave ? shiftEnd.AddMinutes(-rng.Next(15, 60)) : shiftEnd.AddMinutes(rng.Next(0, 45));

            record.CheckInTime = checkIn;
            record.CheckOutTime = checkOut;
            record.WorkingHours = Math.Round((decimal)(checkOut - checkIn).TotalHours, 2);
            record.LateMinutes = late ? (int)(checkIn - shiftStart).TotalMinutes : 0;
            record.EarlyLeaveMinutes = earlyLeave ? (int)(shiftEnd - checkOut).TotalMinutes : 0;
            record.Status = late ? "Late" : "Present";
            record.Source = "Web";
            record.CheckInVerificationMethod = "Face";
            record.CheckOutVerificationMethod = "Face";
            record.CheckInVerified = true;
            record.CheckOutVerified = true;
            record.Location = "Văn phòng";
            record.CreatedDate = checkIn;
            record.ModifiedDate = checkOut;
            return record;
        }

        private static LeaveType PickLeaveType(Random rng, List<LeaveType> types)
        {
            var roll = rng.NextDouble();
            var code = roll < 0.7 ? "ANNUAL" : roll < 0.9 ? "SICK" : "UNPAID";
            return types.FirstOrDefault(t => t.LeaveTypeCode.Contains(code)) ?? types[0];
        }

        private static DateOnly NextWeekday(DateOnly day)
        {
            while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                day = day.AddDays(1);
            return day;
        }

        private static List<DateOnly> WeekdaysFrom(DateOnly start, int count)
        {
            var days = new List<DateOnly>();
            for (var day = start; days.Count < count; day = day.AddDays(1))
                if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    days.Add(day);
            return days;
        }
    }
}
