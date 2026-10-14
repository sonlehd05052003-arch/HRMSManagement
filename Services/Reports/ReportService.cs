using ClosedXML.Excel;
using HRManagement.DTOs;
using HRManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Services.Reports
{
    public interface IReportService
    {
        Task<WorkforceReportDto> GetWorkforceAsync(int year, int? departmentId);
        Task<AttendanceReportDto> GetAttendanceAsync(int year, int month, int? departmentId);
        Task<PayrollReportDto> GetPayrollAsync(int year, int? month);

        /// <summary>The same reports as an .xlsx; <paramref name="kind"/> is workforce, attendance or payroll</summary>
        Task<byte[]> ExportAsync(string kind, int year, int? month, int? departmentId);
    }

    /// <summary>
    /// HR reports for the signed-in company: headcount movements, attendance by department and payroll cost.
    /// Everything is read from the tables the rest of the system already keeps; nothing is stored.
    /// </summary>
    public class ReportService : IReportService
    {
        public const string NoDepartment = "Chưa có phòng ban";
        private static readonly string[] LeftStatuses = { "Resigned", "Terminated", "Inactive" };

        private readonly HrmsDbContext _context;

        public ReportService(HrmsDbContext context)
        {
            _context = context;
        }

        private sealed record Person(int Id, int? DepartmentId, string? Gender, DateOnly? DateOfBirth, DateOnly JoinDate,
            DateOnly? ResignationDate, string Status, string Type);

        // On the payroll on that day. Someone marked as gone without a date is left out of every day.
        private static bool Employed(Person p, DateOnly day) =>
            p.JoinDate <= day && (p.ResignationDate is { } left ? left > day : !LeftStatuses.Contains(p.Status));

        private async Task<Dictionary<int, string>> DepartmentNamesAsync() =>
            await _context.Departments.AsNoTracking().ToDictionaryAsync(d => d.DepartmentId, d => d.DepartmentName);

        public async Task<WorkforceReportDto> GetWorkforceAsync(int year, int? departmentId)
        {
            var query = _context.Employees.AsNoTracking();
            if (departmentId.HasValue)
                query = query.Where(e => e.DepartmentId == departmentId);
            var people = await query
                .Select(e => new Person(e.EmployeeId, e.DepartmentId, e.Gender, e.DateOfBirth, e.JoinDate, e.ResignationDate,
                    e.EmploymentStatus, e.EmploymentType))
                .ToListAsync();

            var today = DateOnly.FromDateTime(AppTime.Today);
            var asOf = year < today.Year ? new DateOnly(year, 12, 31) : today;
            var lastMonth = year < today.Year ? 12 : year == today.Year ? today.Month : 0;

            var report = new WorkforceReportDto { Year = year, AsOf = asOf };
            for (var month = 1; month <= lastMonth; month++)
            {
                var first = new DateOnly(year, month, 1);
                var last = first.AddMonths(1).AddDays(-1);
                var end = last < today ? last : today;
                report.Months.Add(new WorkforceMonthDto
                {
                    Month = month,
                    Headcount = people.Count(p => Employed(p, end)),
                    Hires = people.Count(p => p.JoinDate >= first && p.JoinDate <= last),
                    Leavers = people.Count(p => p.ResignationDate >= first && p.ResignationDate <= last)
                });
            }

            report.Hires = report.Months.Sum(m => m.Hires);
            report.Leavers = report.Months.Sum(m => m.Leavers);
            var average = report.Months.Count > 0 ? report.Months.Average(m => m.Headcount) : 0;
            report.TurnoverRate = average > 0 ? Math.Round((decimal)(report.Leavers / average * 100), 1) : 0;

            var current = lastMonth > 0 ? people.Where(p => Employed(p, asOf)).ToList() : new List<Person>();
            report.Headcount = current.Count;

            var departments = await DepartmentNamesAsync();
            report.ByDepartment = Count(current, p => p.DepartmentId is { } id && departments.TryGetValue(id, out var n) ? n : NoDepartment)
                .OrderByDescending(c => c.Count).ToList();
            report.ByGender = Count(current, p => p.Gender switch { "Male" => "Nam", "Female" => "Nữ", "Other" => "Khác", _ => "Chưa rõ" });
            report.ByEmploymentType = Count(current, p => p.Type switch
            {
                "Full-Time" => "Toàn thời gian", "Part-Time" => "Bán thời gian", "Contract" => "Hợp đồng", "Intern" => "Thực tập",
                _ => p.Type
            });
            report.ByAge = Ordered(Count(current, p => AgeBand(p.DateOfBirth, asOf)), AgeBands);
            report.ByTenure = Ordered(Count(current, p => TenureBand(p.JoinDate, asOf)), TenureBands);
            return report;
        }

        private static readonly string[] AgeBands = { "Dưới 25", "25 – 34", "35 – 44", "45 – 54", "Từ 55", "Chưa rõ" };
        private static readonly string[] TenureBands = { "Dưới 1 năm", "1 – 3 năm", "3 – 5 năm", "5 – 10 năm", "Từ 10 năm" };

        private static string AgeBand(DateOnly? birth, DateOnly on)
        {
            if (birth is not { } b) return "Chưa rõ";
            var age = on.Year - b.Year - (on < b.AddYears(on.Year - b.Year) ? 1 : 0);
            return age < 25 ? AgeBands[0] : age < 35 ? AgeBands[1] : age < 45 ? AgeBands[2] : age < 55 ? AgeBands[3] : AgeBands[4];
        }

        private static string TenureBand(DateOnly joined, DateOnly on)
        {
            var years = (on.DayNumber - joined.DayNumber) / 365.25;
            return years < 1 ? TenureBands[0] : years < 3 ? TenureBands[1] : years < 5 ? TenureBands[2] : years < 10 ? TenureBands[3] : TenureBands[4];
        }

        private static List<ReportCountDto> Count<T>(IEnumerable<T> items, Func<T, string> label) =>
            items.GroupBy(label).Select(g => new ReportCountDto { Label = g.Key, Count = g.Count() }).ToList();

        // Every band in its natural order, empty ones included, except an empty "unknown"
        private static List<ReportCountDto> Ordered(List<ReportCountDto> counts, string[] bands) =>
            bands.Select(b => new ReportCountDto { Label = b, Count = counts.FirstOrDefault(c => c.Label == b)?.Count ?? 0 })
                .Where(c => c.Count > 0 || c.Label != "Chưa rõ")
                .ToList();

        public async Task<AttendanceReportDto> GetAttendanceAsync(int year, int month, int? departmentId)
        {
            var from = new DateOnly(year, month, 1);
            var to = from.AddMonths(1).AddDays(-1);
            // Days still to come are not counted; today is left out too, as on the attendance page
            var today = DateOnly.FromDateTime(AppTime.Today);
            var until = to < today ? to : today.AddDays(-1);

            var employeeQuery = _context.Employees.AsNoTracking();
            if (departmentId.HasValue)
                employeeQuery = employeeQuery.Where(e => e.DepartmentId == departmentId);
            var departmentOf = await employeeQuery.Select(e => new { e.EmployeeId, e.DepartmentId })
                .ToDictionaryAsync(e => e.EmployeeId, e => e.DepartmentId);

            var assignments = await _context.ShiftAssignments.AsNoTracking()
                .Where(a => a.Status == "Active" && a.AssignmentDate >= from && a.AssignmentDate <= until)
                .Select(a => new { a.EmployeeId, a.AssignmentDate })
                .ToListAsync();
            var records = await _context.AttendanceRecords.AsNoTracking()
                .Where(r => r.AttendanceDate >= from && r.AttendanceDate <= until)
                .Select(r => new { r.EmployeeId, r.AttendanceDate, r.Status, r.CheckInTime })
                .ToListAsync();
            var leaves = await _context.LeaveRequests.AsNoTracking()
                .Where(l => l.Status == "Approved" && l.StartDate <= until && l.EndDate >= from)
                .Select(l => new { l.EmployeeId, l.StartDate, l.EndDate })
                .ToListAsync();
            var holidays = (await _context.Holidays.AsNoTracking()
                .Where(h => h.HolidayDate >= from && h.HolidayDate <= until)
                .Select(h => h.HolidayDate).ToListAsync()).ToHashSet();

            var recordByDay = records.Where(r => departmentOf.ContainsKey(r.EmployeeId))
                .GroupBy(r => (r.EmployeeId, r.AttendanceDate)).ToDictionary(g => g.Key, g => g.First());
            var onLeave = leaves.Where(l => departmentOf.ContainsKey(l.EmployeeId))
                .SelectMany(l => Enumerable.Range(0, l.EndDate.DayNumber - l.StartDate.DayNumber + 1)
                    .Select(i => (l.EmployeeId, l.StartDate.AddDays(i))))
                .ToHashSet();

            var rows = new Dictionary<int, AttendanceReportRowDto>();
            var employeesIn = new Dictionary<int, HashSet<int>>();
            AttendanceReportRowDto Row(int employeeId)
            {
                var key = departmentOf[employeeId] ?? 0;
                if (!rows.TryGetValue(key, out var row))
                {
                    rows[key] = row = new AttendanceReportRowDto { DepartmentId = departmentOf[employeeId] };
                    employeesIn[key] = new HashSet<int>();
                }
                employeesIn[key].Add(employeeId);
                return row;
            }

            // A shift day is worked, on leave, or absent, the way the attendance page shows it
            foreach (var a in assignments.Where(a => departmentOf.ContainsKey(a.EmployeeId)).DistinctBy(a => (a.EmployeeId, a.AssignmentDate)))
            {
                if (holidays.Contains(a.AssignmentDate)) continue;
                var row = Row(a.EmployeeId);
                row.ScheduledDays++;
                if (recordByDay.TryGetValue((a.EmployeeId, a.AssignmentDate), out var r) && r.CheckInTime != null && r.Status != "Absent")
                {
                    row.WorkedDays++;
                    if (r.Status == "Late") row.LateDays++;
                }
                else if (onLeave.Contains((a.EmployeeId, a.AssignmentDate)))
                    row.LeaveDays++;
                else
                    row.AbsentDays++;
            }
            // Approved overtime requests, as payroll pays them
            var overtime = await _context.OvertimeRequests.AsNoTracking()
                .Where(o => o.Status == "Approved" && o.OvertimeDate >= from && o.OvertimeDate <= until)
                .Select(o => new { o.EmployeeId, o.TotalHours })
                .ToListAsync();
            foreach (var o in overtime.Where(o => departmentOf.ContainsKey(o.EmployeeId)))
                Row(o.EmployeeId).OvertimeHours += o.TotalHours;

            var departments = await DepartmentNamesAsync();
            foreach (var (key, row) in rows)
            {
                row.Department = row.DepartmentId is { } id && departments.TryGetValue(id, out var name) ? name : NoDepartment;
                row.Employees = employeesIn[key].Count;
                Rates(row);
            }

            var total = new AttendanceReportRowDto
            {
                Department = "Tổng",
                Employees = rows.Values.Sum(r => r.Employees),
                ScheduledDays = rows.Values.Sum(r => r.ScheduledDays),
                WorkedDays = rows.Values.Sum(r => r.WorkedDays),
                LateDays = rows.Values.Sum(r => r.LateDays),
                LeaveDays = rows.Values.Sum(r => r.LeaveDays),
                AbsentDays = rows.Values.Sum(r => r.AbsentDays),
                OvertimeHours = rows.Values.Sum(r => r.OvertimeHours)
            };
            Rates(total);

            return new AttendanceReportDto
            {
                Year = year, Month = month, From = from, To = to, Total = total,
                Departments = rows.Values.OrderBy(r => r.Department == NoDepartment).ThenBy(r => r.Department).ToList()
            };
        }

        private static void Rates(AttendanceReportRowDto row)
        {
            row.AttendanceRate = row.ScheduledDays > 0 ? Math.Round(100m * row.WorkedDays / row.ScheduledDays, 1) : 0;
            row.LateRate = row.WorkedDays > 0 ? Math.Round(100m * row.LateDays / row.WorkedDays, 1) : 0;
        }

        private sealed record Pay(int Month, string PeriodStatus, int EmployeeId, int? DepartmentId, decimal BaseSalary,
            decimal Allowances, decimal OvertimePay, decimal Bonus, decimal Gross, decimal Insurance, decimal Tax,
            decimal Deductions, decimal Net);

        private static PayrollReportRowDto Sum(IEnumerable<Pay> rows, string label)
        {
            var list = rows.ToList();
            return new PayrollReportRowDto
            {
                Label = label,
                Employees = list.Select(r => r.EmployeeId).Distinct().Count(),
                BaseSalary = list.Sum(r => r.BaseSalary),
                Allowances = list.Sum(r => r.Allowances),
                OvertimePay = list.Sum(r => r.OvertimePay),
                Bonus = list.Sum(r => r.Bonus),
                Gross = list.Sum(r => r.Gross),
                Insurance = list.Sum(r => r.Insurance),
                Tax = list.Sum(r => r.Tax),
                Deductions = list.Sum(r => r.Deductions),
                Net = list.Sum(r => r.Net)
            };
        }

        public async Task<PayrollReportDto> GetPayrollAsync(int year, int? month)
        {
            // The department is the employee's current one
            var records = await _context.PayrollRecords.AsNoTracking()
                .Where(r => r.Period.Year == year)
                .Select(r => new Pay(r.Period.Month, r.Period.Status, r.EmployeeId, r.Employee.DepartmentId, r.BaseSalary,
                    r.TotalAllowances, r.OvertimePay, r.BonusAmount, r.GrossPay ?? 0, r.InsuranceAmount, r.TaxAmount,
                    r.TotalDeductions, r.NetPay ?? 0))
                .ToListAsync();

            var report = new PayrollReportDto { Year = year, Total = Sum(records, "Cả năm") };
            foreach (var group in records.GroupBy(r => r.Month).OrderBy(g => g.Key))
            {
                var row = Sum(group, $"Tháng {group.Key}");
                row.Month = group.Key;
                row.Status = group.First().PeriodStatus;
                report.Months.Add(row);
            }

            report.Month = month ?? report.Months.LastOrDefault()?.Month;
            if (report.Month is { } m)
            {
                var departments = await DepartmentNamesAsync();
                report.Departments = records.Where(r => r.Month == m)
                    .GroupBy(r => r.DepartmentId)
                    .Select(g =>
                    {
                        var row = Sum(g, g.Key is { } id && departments.TryGetValue(id, out var n) ? n : NoDepartment);
                        row.DepartmentId = g.Key;
                        return row;
                    })
                    .OrderByDescending(r => r.Gross)
                    .ToList();
            }
            return report;
        }

        public async Task<byte[]> ExportAsync(string kind, int year, int? month, int? departmentId)
        {
            using var workbook = new XLWorkbook();
            switch (kind)
            {
                case "workforce":
                {
                    var r = await GetWorkforceAsync(year, departmentId);
                    var sheet = workbook.Worksheets.Add("BienDong");
                    sheet.Cell(1, 1).Value = $"Biến động nhân sự năm {year}";
                    sheet.Cell(2, 1).Value = $"Nhân sự hiện tại: {r.Headcount} · Vào: {r.Hires} · Nghỉ: {r.Leavers} · Tỷ lệ nghỉ việc: {r.TurnoverRate}%";
                    Table(sheet, 4, new[] { "Tháng", "Nhân sự cuối tháng", "Vào làm", "Nghỉ việc" },
                        r.Months.Select(m => new object[] { $"Tháng {m.Month}", m.Headcount, m.Hires, m.Leavers }));
                    var structure = workbook.Worksheets.Add("CoCau");
                    structure.Cell(1, 1).Value = $"Cơ cấu nhân sự ngày {r.AsOf:dd/MM/yyyy}";
                    var at = 3;
                    foreach (var (title, counts) in new[]
                    {
                        ("Phòng ban", r.ByDepartment), ("Giới tính", r.ByGender), ("Độ tuổi", r.ByAge),
                        ("Thâm niên", r.ByTenure), ("Loại hình", r.ByEmploymentType)
                    })
                    {
                        at = Table(structure, at, new[] { title, "Số người" }, counts.Select(c => new object[] { c.Label, c.Count })) + 2;
                    }
                    break;
                }
                case "attendance":
                {
                    var r = await GetAttendanceAsync(year, month ?? 1, departmentId);
                    var sheet = workbook.Worksheets.Add("ChamCong");
                    sheet.Cell(1, 1).Value = $"Chấm công theo phòng ban tháng {r.Month}/{r.Year}";
                    Table(sheet, 3, new[] { "Phòng ban", "Nhân viên", "Ngày theo ca", "Ngày đi làm", "Đi muộn", "Nghỉ phép", "Vắng", "Giờ tăng ca", "Tỷ lệ đi làm (%)", "Tỷ lệ đi muộn (%)" },
                        r.Departments.Append(r.Total).Select(d => new object[]
                        {
                            d.Department, d.Employees, d.ScheduledDays, d.WorkedDays, d.LateDays, d.LeaveDays, d.AbsentDays,
                            d.OvertimeHours, d.AttendanceRate, d.LateRate
                        }));
                    break;
                }
                case "payroll":
                {
                    var r = await GetPayrollAsync(year, month);
                    var headers = new[] { "", "Nhân viên", "Lương cơ bản", "Phụ cấp", "Tăng ca", "Thưởng", "Tổng thu nhập", "Bảo hiểm", "Thuế TNCN", "Tổng khấu trừ", "Thực lĩnh" };
                    object[] Values(PayrollReportRowDto d) => new object[]
                    {
                        d.Label, d.Employees, d.BaseSalary, d.Allowances, d.OvertimePay, d.Bonus, d.Gross, d.Insurance, d.Tax, d.Deductions, d.Net
                    };
                    var sheet = workbook.Worksheets.Add("TheoThang");
                    sheet.Cell(1, 1).Value = $"Chi phí lương năm {year}";
                    Table(sheet, 3, headers, r.Months.Append(r.Total).Select(Values));
                    if (r.Month is { } m)
                    {
                        var byDepartment = workbook.Worksheets.Add("TheoPhongBan");
                        byDepartment.Cell(1, 1).Value = $"Chi phí lương theo phòng ban tháng {m}/{year}";
                        Table(byDepartment, 3, headers, r.Departments.Select(Values));
                    }
                    foreach (var ws in workbook.Worksheets)
                        ws.Columns(3, 11).Style.NumberFormat.Format = "#,##0";
                    break;
                }
                default:
                    throw new ArgumentException($"Không có báo cáo '{kind}'.");
            }

            foreach (var ws in workbook.Worksheets)
            {
                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(1, 1).Style.Font.FontSize = 14;
                ws.Columns().AdjustToContents();
            }
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        /// <summary>Writes a header row and the rows from <paramref name="row"/>; returns the last row used.</summary>
        private static int Table(IXLWorksheet sheet, int row, string[] headers, IEnumerable<object[]> rows)
        {
            for (var c = 0; c < headers.Length; c++)
                sheet.Cell(row, c + 1).Value = headers[c];
            var header = sheet.Range(row, 1, row, headers.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E0E7FF");
            foreach (var values in rows)
            {
                row++;
                for (var c = 0; c < values.Length; c++)
                    sheet.Cell(row, c + 1).Value = XLCellValue.FromObject(values[c]);
            }
            return row;
        }
    }
}
