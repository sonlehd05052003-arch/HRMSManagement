using ClosedXML.Excel;
using HRManagement.Models;
using HRManagement.Services.Employees;
using HRManagement.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Tests.Employees
{
    public class EmployeeImportServiceTests
    {
        private readonly DbContextOptions<HrmsDbContext> _options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        public EmployeeImportServiceTests()
        {
            using var db = NewContext();
            db.Departments.Add(new Department { DepartmentCode = "KD", DepartmentName = "Kinh doanh", IsActive = true });
            db.Positions.Add(new Position { PositionCode = "NV", PositionName = "Nhân viên", Level = 5, IsActive = true });
            db.Employees.Add(new Employee
            {
                EmployeeCode = "EMP001", FirstName = "Bình", LastName = "Trần", FullName = "Bình Trần", Email = "binh@test.vn",
                EmploymentStatus = "Active", EmploymentType = "Full-Time", JoinDate = new DateOnly(2020, 1, 1)
            });
            db.SaveChanges();
        }

        private HrmsDbContext NewContext()
        {
            var tenant = new TenantContext(new HttpContextAccessor());
            tenant.Use(1);
            return new HrmsDbContext(_options, tenant);
        }

        // Columns: code, last name, first name, email, phone, gender, birth, department, position, manager, join date,
        // type, base salary, insurance salary, dependents, address, city
        private static MemoryStream File(params object?[][] rows)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("NhanVien");
            sheet.Cell(1, 1).Value = "header";
            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++)
                    sheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
            var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;
            return stream;
        }

        private static object?[] Person(string? code, string email, string? manager = null, string join = "01/06/2023",
            string birth = "15/03/1995", string? department = "KD") =>
            new object?[] { code, "Nguyễn Văn", "An", email, "0901234567", "Nam", birth, department, "NV", manager, join, "", 15000000, 15000000, 1 };

        [Fact]
        public async Task The_template_example_row_passes_the_check()
        {
            using var db = NewContext();
            var service = new EmployeeImportService(db);
            using var template = new MemoryStream(await service.BuildTemplateAsync());

            var result = await service.ImportAsync(template, dryRun: true, createdBy: null);

            Assert.Equal(1, result.TotalRows);
            Assert.Equal(1, result.ValidRows);
            Assert.Empty(result.Errors);
            Assert.Equal(1, await db.Employees.CountAsync());
        }

        [Fact]
        public async Task Each_bad_row_is_reported_with_its_reasons()
        {
            using var db = NewContext();
            var service = new EmployeeImportService(db);
            using var file = File(
                Person(null, "ok@test.vn"),
                Person(null, "binh@test.vn"),                        // email already used
                Person("EMP001", "x@test.vn"),                       // code already used
                Person(null, "dup@test.vn"), Person(null, "dup@test.vn"),
                Person(null, "young@test.vn", birth: "01/01/2010"),  // 13 when joining
                Person(null, "dept@test.vn", department: "XX"),
                Person(null, "boss@test.vn", manager: "EMP999"),
                Person(null, "date@test.vn", join: "31/02/2023"));

            var result = await service.ImportAsync(file, dryRun: false, createdBy: null);

            Assert.Equal(9, result.TotalRows);
            Assert.Equal(1, result.ValidRows);
            Assert.Equal(new[] { 3, 4, 5, 6, 7, 8, 9, 10 }, result.Errors.Select(e => e.Row));
            Assert.Contains("đã có nhân viên khác dùng", result.Errors[0].Messages[0]);
            Assert.Contains("đã tồn tại", result.Errors[1].Messages[0]);
            Assert.Contains("lặp lại trong file", result.Errors[2].Messages[0]);
            Assert.Contains("chưa đủ 18 tuổi", result.Errors[4].Messages[0]);
            Assert.Contains("XX", result.Errors[5].Messages[0]);
            Assert.Contains("EMP999", result.Errors[6].Messages[0]);
            Assert.Contains(result.Errors[7].Messages, m => m.Contains("Ngày vào làm"));
            // Any error and nothing is added
            Assert.Equal(0, result.Imported);
            Assert.Equal(1, await db.Employees.CountAsync());
        }

        [Fact]
        public async Task A_clean_file_adds_everyone_with_codes_and_managers()
        {
            using var db = NewContext();
            var service = new EmployeeImportService(db);
            using var file = File(
                Person("TP01", "lead@test.vn", manager: "EMP001"),
                Person(null, "a@test.vn", manager: "TP01"),          // manager further up the same file
                Person(null, "b@test.vn"));

            var result = await service.ImportAsync(file, dryRun: false, createdBy: 7);

            Assert.Empty(result.Errors);
            Assert.Equal(3, result.Imported);
            var added = await db.Employees.Where(e => e.EmployeeCode != "EMP001").OrderBy(e => e.EmployeeId).ToListAsync();
            var lead = added.Single(e => e.EmployeeCode == "TP01");
            var existing = await db.Employees.SingleAsync(e => e.EmployeeCode == "EMP001");
            Assert.Equal(existing.EmployeeId, lead.ManagerId);
            // Blank codes take the next free EMPnnn, skipping EMP001
            Assert.Equal(new[] { "TP01", "EMP002", "EMP003" }, added.Select(e => e.EmployeeCode));
            Assert.Equal(lead.EmployeeId, added[1].ManagerId);
            Assert.Null(added[2].ManagerId);
            Assert.All(added, e =>
            {
                Assert.Equal("Nguyễn Văn", e.FirstName);
                Assert.Equal("An", e.LastName);
                Assert.Equal("Active", e.EmploymentStatus);
                Assert.Equal("Full-Time", e.EmploymentType);
                Assert.Equal("Male", e.Gender);
                Assert.Equal(new DateOnly(2023, 6, 1), e.JoinDate);
                Assert.Equal(15_000_000m, e.BaseSalary);
                Assert.Equal(1, e.NumberOfDependents);
                Assert.Equal(7, e.CreatedBy);
            });
        }

        [Fact]
        public async Task A_dry_run_adds_nothing()
        {
            using var db = NewContext();
            var service = new EmployeeImportService(db);
            using var file = File(Person(null, "a@test.vn"));

            var result = await service.ImportAsync(file, dryRun: true, createdBy: null);

            Assert.Equal(1, result.ValidRows);
            Assert.Equal(0, result.Imported);
            Assert.Equal(1, await db.Employees.CountAsync());
        }

        [Fact]
        public async Task A_file_that_is_not_a_workbook_is_refused()
        {
            using var db = NewContext();
            var service = new EmployeeImportService(db);
            using var junk = new MemoryStream("not excel"u8.ToArray());

            await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync(junk, dryRun: true, createdBy: null));
        }
    }
}
