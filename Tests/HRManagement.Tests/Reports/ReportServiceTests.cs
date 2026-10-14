using HRManagement.Models;
using HRManagement.Services.Reports;
using HRManagement.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Tests.Reports
{
    public class ReportServiceTests
    {
        private readonly DbContextOptions<HrmsDbContext> _options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        // A finished year, so nothing depends on the day the tests run
        private static readonly int Year = AppTime.Today.Year - 1;
        private const int Sales = 1, Tech = 2;

        public ReportServiceTests()
        {
            using var db = NewContext();
            db.Departments.AddRange(
                new Department { DepartmentId = Sales, DepartmentCode = "KD", DepartmentName = "Kinh doanh", IsActive = true },
                new Department { DepartmentId = Tech, DepartmentCode = "KT", DepartmentName = "Kỹ thuật", IsActive = true });
            db.SaveChanges();
        }

        private HrmsDbContext NewContext()
        {
            var tenant = new TenantContext(new HttpContextAccessor());
            tenant.Use(1);
            return new HrmsDbContext(_options, tenant);
        }

        private static Employee Person(int id, int? department, DateOnly joined, DateOnly? left = null, string status = "Active",
            string? gender = "Male", DateOnly? born = null) => new()
        {
            EmployeeId = id, EmployeeCode = $"E{id}", FirstName = "Nguyễn", LastName = $"An {id}", FullName = $"Nguyễn An {id}",
            Email = $"e{id}@test.vn", DepartmentId = department, JoinDate = joined, ResignationDate = left, EmploymentStatus = status,
            EmploymentType = "Full-Time", Gender = gender, DateOfBirth = born
        };

        [Fact]
        public async Task Headcount_hires_and_leavers_are_counted_per_month()
        {
            using (var db = NewContext())
            {
                db.Employees.AddRange(
                    Person(1, Sales, new DateOnly(Year - 3, 5, 1), born: new DateOnly(Year - 30, 1, 1)),
                    Person(2, Sales, new DateOnly(Year, 3, 10), gender: "Female"),                          // hired in March
                    Person(3, Tech, new DateOnly(Year - 1, 1, 1), new DateOnly(Year, 6, 15), "Resigned"),   // left in June
                    Person(4, Tech, new DateOnly(Year - 1, 1, 1), status: "Terminated"),                     // gone, no date
                    Person(5, Tech, new DateOnly(Year + 1, 1, 1)));                                          // joins next year
                await db.SaveChangesAsync();
            }
            using var context = NewContext();

            var report = await new ReportService(context).GetWorkforceAsync(Year, null);

            Assert.Equal(12, report.Months.Count);
            Assert.Equal(new[] { 2, 2, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2 }, report.Months.Select(m => m.Headcount));
            Assert.Equal(1, report.Months.Single(m => m.Month == 3).Hires);
            Assert.Equal(1, report.Months.Single(m => m.Month == 6).Leavers);
            Assert.Equal(2, report.Headcount);
            Assert.Equal(new DateOnly(Year, 12, 31), report.AsOf);
            // 1 leaver over an average headcount of 27 / 12
            Assert.Equal(Math.Round(1m / (27m / 12) * 100, 1), report.TurnoverRate);
            Assert.Equal(2, report.ByDepartment.Single(d => d.Label == "Kinh doanh").Count);
            Assert.DoesNotContain(report.ByDepartment, d => d.Label == "Kỹ thuật");
            Assert.Equal(1, report.ByGender.Single(g => g.Label == "Nữ").Count);
            Assert.Equal(1, report.ByAge.Single(a => a.Label == "25 – 34").Count);
            Assert.Equal(1, report.ByAge.Single(a => a.Label == "Chưa rõ").Count);
            Assert.Equal(new[] { 1, 0, 1, 0, 0 }, report.ByTenure.Select(t => t.Count));
        }

        [Fact]
        public async Task A_department_filter_keeps_only_its_people()
        {
            using (var db = NewContext())
            {
                db.Employees.AddRange(Person(1, Sales, new DateOnly(Year - 1, 1, 1)), Person(2, Tech, new DateOnly(Year - 1, 1, 1)));
                await db.SaveChangesAsync();
            }
            using var context = NewContext();

            var report = await new ReportService(context).GetWorkforceAsync(Year, Tech);

            Assert.Equal(1, report.Headcount);
            Assert.Equal("Kỹ thuật", Assert.Single(report.ByDepartment).Label);
        }

        [Fact]
        public async Task Shift_days_are_split_into_worked_late_leave_and_absent()
        {
            var day = (int d) => new DateOnly(Year, 3, d);
            using (var db = NewContext())
            {
                db.Employees.AddRange(Person(1, Sales, new DateOnly(Year - 1, 1, 1)), Person(2, Tech, new DateOnly(Year - 1, 1, 1)));
                // Employee 1 has a shift on 2–6 March; employee 2 on 2–3 March
                foreach (var d in Enumerable.Range(2, 5))
                    db.ShiftAssignments.Add(new ShiftAssignment { EmployeeId = 1, ShiftId = 1, AssignmentDate = day(d), StartDate = day(d), Status = "Active" });
                foreach (var d in Enumerable.Range(2, 2))
                    db.ShiftAssignments.Add(new ShiftAssignment { EmployeeId = 2, ShiftId = 1, AssignmentDate = day(d), StartDate = day(d), Status = "Active" });
                db.AttendanceRecords.AddRange(
                    new AttendanceRecord { EmployeeId = 1, AttendanceDate = day(2), CheckInTime = new DateTime(Year, 3, 2, 8, 0, 0), Status = "Present" },
                    new AttendanceRecord { EmployeeId = 1, AttendanceDate = day(3), CheckInTime = new DateTime(Year, 3, 3, 8, 30, 0), Status = "Late" },
                    new AttendanceRecord { EmployeeId = 2, AttendanceDate = day(2), CheckInTime = new DateTime(Year, 3, 2, 8, 0, 0), Status = "Present" });
                // 4 March on leave, 5 March a holiday, 6 March nothing: absent
                db.LeaveRequests.Add(new LeaveRequest { RequestNumber = "L1", EmployeeId = 1, LeaveTypeId = 1, StartDate = day(4), EndDate = day(4), NumberOfDays = 1, Status = "Approved" });
                db.Holidays.Add(new Holiday { HolidayDate = day(5), Name = "Lễ", IsPaid = true });
                db.OvertimeRequests.AddRange(
                    new OvertimeRequest { RequestNumber = "O1", EmployeeId = 1, OvertimeDate = day(2), TotalHours = 2.5m, Status = "Approved" },
                    new OvertimeRequest { RequestNumber = "O2", EmployeeId = 1, OvertimeDate = day(3), TotalHours = 4, Status = "Pending" });
                await db.SaveChangesAsync();
            }
            using var context = NewContext();

            var report = await new ReportService(context).GetAttendanceAsync(Year, 3, null);

            var sales = report.Departments.Single(d => d.DepartmentId == Sales);
            Assert.Equal(4, sales.ScheduledDays);
            Assert.Equal(2, sales.WorkedDays);
            Assert.Equal(1, sales.LateDays);
            Assert.Equal(1, sales.LeaveDays);
            Assert.Equal(1, sales.AbsentDays);
            Assert.Equal(2.5m, sales.OvertimeHours);
            Assert.Equal(50, sales.AttendanceRate);
            Assert.Equal(50, sales.LateRate);
            var tech = report.Departments.Single(d => d.DepartmentId == Tech);
            Assert.Equal((2, 1, 1), (tech.ScheduledDays, tech.WorkedDays, tech.AbsentDays));
            Assert.Equal(6, report.Total.ScheduledDays);
            Assert.Equal(2, report.Total.Employees);
        }

        [Fact]
        public async Task Payroll_is_summed_per_month_and_per_department()
        {
            using (var db = NewContext())
            {
                db.Employees.AddRange(Person(1, Sales, new DateOnly(Year - 1, 1, 1)), Person(2, Tech, new DateOnly(Year - 1, 1, 1)));
                db.PayrollPeriods.AddRange(
                    new PayrollPeriod { PeriodId = 1, Year = Year, Month = 1, Status = "Approved", StartDate = new DateOnly(Year, 1, 1), EndDate = new DateOnly(Year, 1, 31) },
                    new PayrollPeriod { PeriodId = 2, Year = Year, Month = 2, Status = "Calculated", StartDate = new DateOnly(Year, 2, 1), EndDate = new DateOnly(Year, 2, 28) });
                PayrollRecord Pay(int employee, int period, decimal gross, decimal net) => new()
                {
                    EmployeeId = employee, PeriodId = period, BaseSalary = gross, GrossPay = gross, NetPay = net, InsuranceAmount = gross - net, Status = "Calculated"
                };
                db.PayrollRecords.AddRange(Pay(1, 1, 10_000_000, 9_000_000), Pay(2, 1, 20_000_000, 18_000_000), Pay(1, 2, 12_000_000, 11_000_000));
                await db.SaveChangesAsync();
            }
            using var context = NewContext();

            var report = await new ReportService(context).GetPayrollAsync(Year, null);

            Assert.Equal(new[] { 30_000_000m, 12_000_000m }, report.Months.Select(m => m.Gross));
            Assert.Equal(new[] { "Approved", "Calculated" }, report.Months.Select(m => m.Status));
            Assert.Equal(42_000_000m, report.Total.Gross);
            Assert.Equal(38_000_000m, report.Total.Net);
            Assert.Equal(2, report.Total.Employees);
            // The department split defaults to the latest month
            Assert.Equal(2, report.Month);
            Assert.Equal("Kinh doanh", Assert.Single(report.Departments).Label);

            var january = await new ReportService(context).GetPayrollAsync(Year, 1);
            Assert.Equal(new[] { "Kỹ thuật", "Kinh doanh" }, january.Departments.Select(d => d.Label));
        }

        [Fact]
        public async Task Every_report_exports_to_excel()
        {
            using var context = NewContext();
            var service = new ReportService(context);

            foreach (var kind in new[] { "workforce", "attendance", "payroll" })
                Assert.NotEmpty(await service.ExportAsync(kind, Year, 3, null));
            await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync("other", Year, 3, null));
        }
    }
}
