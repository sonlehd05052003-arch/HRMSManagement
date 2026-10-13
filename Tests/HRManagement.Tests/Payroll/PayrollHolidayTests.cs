using AutoMapper;
using HRManagement.DataAcess.Implementations;
using HRManagement.Mappers;
using HRManagement.Models;
using HRManagement.Services.Emails;
using HRManagement.Services.Holidays;
using HRManagement.Services.Notifications;
using HRManagement.Services.Payroll;
using HRManagement.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Tests.Payroll
{
    /// <summary>
    /// April 2026: every weekday has a shift, including 30/4; the employee works every day except 30/4
    /// and does 2 hours of approved overtime on 30/4.
    /// </summary>
    public class PayrollHolidayTests
    {
        private const decimal BaseSalary = 22_000_000m;
        private static readonly DateOnly Holiday = new(2026, 4, 30);

        private sealed class NoEmail : IEmailService
        {
            public Task SendAsync(string to, string subject, string body) => Task.CompletedTask;
            public Task SendWithAttachmentAsync(string to, string subject, string body, byte[] fileBytes, string fileName, string contentType) => Task.CompletedTask;
        }

        private readonly DbContextOptions<HrmsDbContext> _options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        private HrmsDbContext NewContext()
        {
            var tenant = new TenantContext(new HttpContextAccessor());
            tenant.Use(1);
            return new HrmsDbContext(_options, tenant);
        }

        private static IEnumerable<DateOnly> Weekdays()
        {
            for (var d = new DateOnly(2026, 4, 1); d <= new DateOnly(2026, 4, 30); d = d.AddDays(1))
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    yield return d;
        }

        private (int EmployeeId, int PeriodId) Seed(bool holidayOnFile)
        {
            using var db = NewContext();
            var shift = new Shift
            {
                ShiftCode = "HC", ShiftName = "Hành chính", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0),
                WorkingHours = 8, ShiftType = "Regular", IsActive = true
            };
            var employee = new Employee
            {
                EmployeeCode = "E1", FirstName = "An", LastName = "Nguyễn", FullName = "An Nguyễn", Email = "an@test",
                EmploymentStatus = "Active", EmploymentType = "Full-Time", JoinDate = new DateOnly(2025, 1, 1), BaseSalary = BaseSalary
            };
            var period = new PayrollPeriod
            {
                Month = 4, Year = 2026, StartDate = new DateOnly(2026, 4, 1), EndDate = new DateOnly(2026, 4, 30),
                Status = "Draft", AttendanceCutoffDate = new DateOnly(2026, 4, 30)
            };
            db.AddRange(shift, employee, period);
            db.SaveChanges();

            foreach (var day in Weekdays())
            {
                db.ShiftAssignments.Add(new ShiftAssignment
                {
                    EmployeeId = employee.EmployeeId, ShiftId = shift.ShiftId, AssignmentDate = day, StartDate = day, Status = "Active"
                });
                if (day != Holiday)
                    db.AttendanceRecords.Add(new AttendanceRecord
                    {
                        EmployeeId = employee.EmployeeId, AttendanceDate = day, ShiftId = shift.ShiftId, Status = "Present",
                        CheckInTime = day.ToDateTime(new TimeOnly(8, 0)), CheckOutTime = day.ToDateTime(new TimeOnly(17, 0)), WorkingHours = 8
                    });
            }
            db.OvertimeRequests.Add(new OvertimeRequest
            {
                RequestNumber = "OT-1", EmployeeId = employee.EmployeeId, OvertimeDate = Holiday,
                StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(11, 0), TotalHours = 2, Status = "Approved", SubmittedDate = DateTime.Now
            });
            if (holidayOnFile)
                db.Holidays.Add(new Holiday { HolidayDate = Holiday, Name = "Ngày Chiến thắng", IsPaid = true });
            db.SaveChanges();

            return (employee.EmployeeId, period.PeriodId);
        }

        private PayrollService NewPayroll(HrmsDbContext db) => new(
            new PayrollRepository(db), new PayrollPeriodRepository(db), new TaxCalculationService(), db,
            new MapperConfiguration(cfg => cfg.AddProfile<PayrollProfile>()).CreateMapper(),
            new NoEmail(), new NotificationService(db));

        [Fact]
        public async Task A_paid_holiday_is_a_full_paid_day_and_overtime_on_it_uses_the_holiday_rate()
        {
            var (employeeId, periodId) = Seed(holidayOnFile: true);
            using var db = NewContext();

            var record = await NewPayroll(db).CalculateForEmployeeAsync(employeeId, periodId);

            var weekdays = Weekdays().Count();
            Assert.Equal(weekdays, record.WorkingDays);
            Assert.Equal(weekdays, record.ActualWorkingDays);
            Assert.Equal(BaseSalary, record.SalariedAmount);
            var hourly = BaseSalary / (weekdays * 8);
            Assert.Equal(Math.Round(hourly * 2 * 3.0m, 0), record.OvertimePay);
        }

        [Fact]
        public async Task Without_the_holiday_on_file_the_day_counts_as_missed_and_overtime_as_a_weekday()
        {
            var (employeeId, periodId) = Seed(holidayOnFile: false);
            using var db = NewContext();

            var record = await NewPayroll(db).CalculateForEmployeeAsync(employeeId, periodId);

            var weekdays = Weekdays().Count();
            Assert.Equal(weekdays - 1, record.ActualWorkingDays);
            Assert.True(record.SalariedAmount < BaseSalary);
            var hourly = BaseSalary / (weekdays * 8);
            Assert.Equal(Math.Round(hourly * 2 * 1.5m, 0), record.OvertimePay);
        }
    }

    public class HolidayServiceTests
    {
        private readonly DbContextOptions<HrmsDbContext> _options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        private HrmsDbContext NewContext()
        {
            var tenant = new TenantContext(new HttpContextAccessor());
            tenant.Use(1);
            return new HrmsDbContext(_options, tenant);
        }

        [Fact]
        public async Task Fixed_Vietnam_holidays_are_added_once()
        {
            using var db = NewContext();
            var service = new HolidayService(db);

            var first = await service.AddFixedVietnamHolidaysAsync(2026, null);
            var second = await service.AddFixedVietnamHolidaysAsync(2026, null);
            var year = await service.GetByYearAsync(2026);

            Assert.Equal(4, first);
            Assert.Equal(0, second);
            Assert.Equal(new[] { "01-01", "04-30", "05-01", "09-02" }, year.Select(h => h.HolidayDate.ToString("MM-dd")));
        }

        [Fact]
        public async Task The_same_day_cannot_be_entered_twice()
        {
            using var db = NewContext();
            var service = new HolidayService(db);
            await service.CreateAsync(new DTOs.HolidayWriteDto { HolidayDate = new DateOnly(2026, 2, 17), Name = "Mùng 1 Tết" }, null);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new DTOs.HolidayWriteDto { HolidayDate = new DateOnly(2026, 2, 17), Name = "Trùng" }, null));
        }
    }
}
