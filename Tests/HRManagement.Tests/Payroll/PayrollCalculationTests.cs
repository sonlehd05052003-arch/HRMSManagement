using System.Globalization;
using AutoMapper;
using HRManagement.DataAcess.Implementations;
using HRManagement.Mappers;
using HRManagement.Models;
using HRManagement.Services.Emails;
using HRManagement.Services.Notifications;
using HRManagement.Services.Payroll;
using HRManagement.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Tests.Payroll
{
    /// <summary>
    /// April 2026, a shift every weekday. The employee checks in at 7:45 and out at 17:30, which the attendance
    /// screen records as 9.75 hours on an 8-hour shift, and misses one day.
    /// </summary>
    public class PayrollCalculationTests
    {
        private const decimal BaseSalary = 22_000_000m;
        private static readonly DateOnly Missed = new(2026, 4, 15);

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

        private static List<DateOnly> Weekdays()
        {
            var days = new List<DateOnly>();
            for (var d = new DateOnly(2026, 4, 1); d <= new DateOnly(2026, 4, 30); d = d.AddDays(1))
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    days.Add(d);
            return days;
        }

        private (int EmployeeId, int PeriodId) Seed()
        {
            using var db = NewContext();
            var shift = new Shift
            {
                ShiftCode = "HC", ShiftName = "Hành chính", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0),
                WorkingHours = 8, ShiftType = "Regular", IsActive = true
            };
            var employee = new Employee
            {
                EmployeeCode = "E1", FirstName = "Nguyễn", LastName = "An", FullName = "Nguyễn An", Email = "an@test",
                EmploymentStatus = "Active", EmploymentType = "Full-Time", JoinDate = new DateOnly(2025, 1, 1), BaseSalary = BaseSalary
            };
            var period = new PayrollPeriod
            {
                Month = 4, Year = 2026, StartDate = new DateOnly(2026, 4, 1), EndDate = new DateOnly(2026, 4, 30),
                Status = "Open", AttendanceCutoffDate = new DateOnly(2026, 4, 30)
            };
            db.AddRange(shift, employee, period);
            // Rates are percentages, as the settings page saves them
            db.SystemSettings.AddRange(
                new SystemSetting { SettingKey = "Payroll.Calc.BhxhRate", SettingValue = "8", SettingCategory = "Payroll" },
                new SystemSetting { SettingKey = "Payroll.Calc.BhytRate", SettingValue = "1.5", SettingCategory = "Payroll" },
                new SystemSetting { SettingKey = "Payroll.Calc.BhtnRate", SettingValue = "1", SettingCategory = "Payroll" });
            db.SaveChanges();

            foreach (var day in Weekdays())
            {
                db.ShiftAssignments.Add(new ShiftAssignment
                {
                    EmployeeId = employee.EmployeeId, ShiftId = shift.ShiftId, AssignmentDate = day, StartDate = day, Status = "Active"
                });
                if (day != Missed)
                    db.AttendanceRecords.Add(new AttendanceRecord
                    {
                        EmployeeId = employee.EmployeeId, AttendanceDate = day, ShiftId = shift.ShiftId, Status = "Present",
                        CheckInTime = day.ToDateTime(new TimeOnly(7, 45)), CheckOutTime = day.ToDateTime(new TimeOnly(17, 30)), WorkingHours = 9.75m
                    });
            }
            db.SaveChanges();
            return (employee.EmployeeId, period.PeriodId);
        }

        private static PayrollService NewPayroll(HrmsDbContext db) => new(
            new PayrollRepository(db), new PayrollPeriodRepository(db), new TaxCalculationService(), db,
            new MapperConfiguration(cfg => cfg.AddProfile<PayrollProfile>()).CreateMapper(),
            new NoEmail(), new NotificationService(db));

        [Fact]
        public async Task A_day_counts_once_however_long_and_a_missed_day_is_not_paid()
        {
            var (employeeId, periodId) = Seed();
            using var db = NewContext();

            var record = await NewPayroll(db).CalculateForEmployeeAsync(employeeId, periodId);

            var weekdays = Weekdays().Count;
            Assert.Equal(weekdays, record.WorkingDays);
            Assert.Equal(weekdays - 1, record.ActualWorkingDays);
            Assert.Equal(Math.Round(BaseSalary / weekdays * (weekdays - 1), 0), record.SalariedAmount);
        }

        [Fact]
        public async Task Net_pay_is_gross_minus_insurance_and_tax_taken_once()
        {
            var (employeeId, periodId) = Seed();
            using var db = NewContext();

            var record = await NewPayroll(db).CalculateForEmployeeAsync(employeeId, periodId);

            // 8 % + 1.5 % + 1 % of the base salary
            Assert.Equal(Math.Round(BaseSalary * 0.105m, 0), record.InsuranceAmount);
            Assert.Equal(record.SalariedAmount + record.TotalAllowances + record.OvertimePay + record.BonusAmount, record.GrossPay);
            Assert.Equal(record.InsuranceAmount + record.TaxAmount, record.TotalDeductions);
            Assert.Equal(record.GrossPay - record.TotalDeductions, record.NetPay);
            var saved = await db.PayrollRecords.AsNoTracking().SingleAsync();
            Assert.Equal(record.GrossPay, saved.GrossPay);
            Assert.Equal(record.NetPay, saved.NetPay);
        }

        [Fact]
        public async Task Rates_read_the_same_on_a_server_set_to_Vietnamese()
        {
            var (employeeId, periodId) = Seed();
            using var db = NewContext();
            var culture = CultureInfo.CurrentCulture;
            try
            {
                // vi-VN reads "1.5" as fifteen if parsed with the server culture
                CultureInfo.CurrentCulture = new CultureInfo("vi-VN");
                var record = await NewPayroll(db).CalculateForEmployeeAsync(employeeId, periodId);
                Assert.Equal(Math.Round(BaseSalary * 0.105m, 0), record.InsuranceAmount);
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        }

        [Fact]
        public async Task A_manual_allowance_and_bonus_change_gross_and_net_together()
        {
            var (employeeId, periodId) = Seed();
            using var db = NewContext();
            var payroll = NewPayroll(db);
            var record = await payroll.CalculateForEmployeeAsync(employeeId, periodId);

            await payroll.AddAllowanceAsync(record.PayrollRecordId, new DTOs.Payroll.CreatePayrollAllowanceDto
            {
                AllowanceType = "Manual", AllowanceName = "Phụ cấp dự án", Amount = 500_000
            });
            var after = await payroll.UpdateBonusAsync(record.PayrollRecordId, 1_000_000);

            Assert.Equal(record.GrossPay + 1_500_000, after.GrossPay);
            Assert.Equal(record.NetPay + 1_500_000, after.NetPay);
        }

        [Fact]
        public void Gross_and_net_pay_are_stored_not_computed_by_the_database()
        {
            using var db = NewContext();
            var entity = db.Model.FindEntityType(typeof(PayrollRecord))!;

            Assert.Null(entity.FindProperty(nameof(PayrollRecord.GrossPay))!.GetComputedColumnSql());
            Assert.Null(entity.FindProperty(nameof(PayrollRecord.NetPay))!.GetComputedColumnSql());
        }
    }
}
