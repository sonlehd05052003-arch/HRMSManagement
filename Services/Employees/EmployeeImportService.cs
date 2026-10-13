using System.Globalization;
using System.Net.Mail;
using ClosedXML.Excel;
using HRManagement.DTOs;
using HRManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Services.Employees
{
    public interface IEmployeeImportService
    {
        /// <summary>An .xlsx to fill in: the employee sheet with an example row, and a sheet of the company's codes.</summary>
        Task<byte[]> BuildTemplateAsync();

        /// <summary>
        /// Checks every row of an uploaded file. With <paramref name="dryRun"/> false and no row in error, adds all the
        /// employees in one transaction; any error means nothing is added.
        /// </summary>
        Task<EmployeeImportResultDto> ImportAsync(Stream file, bool dryRun, int? createdBy);
    }

    /// <summary>
    /// Bulk import of existing staff from Excel, for companies starting on the system. Unlike the single "add
    /// employee" form, a join date in the past is accepted: these people already work there.
    /// </summary>
    public class EmployeeImportService : IEmployeeImportService
    {
        public const int MaxRows = 2000;
        private const string SheetName = "NhanVien";

        // Column order of the template; the header row is row 1, data starts at row 2
        private static readonly string[] Headers =
        {
            "Mã NV (để trống: tự sinh)", "Họ và tên đệm *", "Tên *", "Email *", "Số điện thoại", "Giới tính (Nam/Nữ/Khác)",
            "Ngày sinh (dd/MM/yyyy)", "Mã phòng ban", "Mã chức vụ", "Mã NV quản lý", "Ngày vào làm * (dd/MM/yyyy)",
            "Loại hình (Full-Time/Part-Time/Contract/Intern)", "Lương cơ bản", "Lương đóng BH", "Số người phụ thuộc",
            "Địa chỉ", "Thành phố"
        };

        private static readonly string[] EmploymentTypes = { "Full-Time", "Part-Time", "Contract", "Intern" };
        private static readonly Dictionary<string, string> Genders = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Nam"] = "Male", ["Male"] = "Male", ["Nữ"] = "Female", ["Nu"] = "Female", ["Female"] = "Female",
            ["Khác"] = "Other", ["Khac"] = "Other", ["Other"] = "Other"
        };
        private static readonly string[] DateFormats = { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" };

        private readonly HrmsDbContext _context;

        public EmployeeImportService(HrmsDbContext context)
        {
            _context = context;
        }

        public async Task<byte[]> BuildTemplateAsync()
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add(SheetName);
            for (var c = 0; c < Headers.Length; c++)
                sheet.Cell(1, c + 1).Value = Headers[c];
            var header = sheet.Range(1, 1, 1, Headers.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E0E7FF");

            object[] example =
            {
                "", "Nguyễn Văn", "An", "an.nguyen@congty.vn", "0901234567", "Nam", "15/03/1995", "", "", "", "01/06/2023",
                "Full-Time", 15000000, 15000000, 0, "12 Lê Lợi", "Hà Nội"
            };
            for (var c = 0; c < example.Length; c++)
                sheet.Cell(2, c + 1).Value = XLCellValue.FromObject(example[c]);
            sheet.Row(2).Style.Font.Italic = true;
            sheet.Row(2).Style.Font.FontColor = XLColor.Gray;
            sheet.Columns().AdjustToContents();
            sheet.SheetView.FreezeRows(1);

            // Codes the file may refer to
            var codes = workbook.Worksheets.Add("DanhMuc");
            codes.Cell(1, 1).Value = "Mã phòng ban";
            codes.Cell(1, 2).Value = "Tên phòng ban";
            codes.Cell(1, 4).Value = "Mã chức vụ";
            codes.Cell(1, 5).Value = "Tên chức vụ";
            codes.Row(1).Style.Font.Bold = true;
            var departments = await _context.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.DepartmentCode).ToListAsync();
            for (var i = 0; i < departments.Count; i++)
            {
                codes.Cell(i + 2, 1).Value = departments[i].DepartmentCode;
                codes.Cell(i + 2, 2).Value = departments[i].DepartmentName;
            }
            var positions = await _context.Positions.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Level).ToListAsync();
            for (var i = 0; i < positions.Count; i++)
            {
                codes.Cell(i + 2, 4).Value = positions[i].PositionCode;
                codes.Cell(i + 2, 5).Value = positions[i].PositionName;
            }
            codes.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private sealed class Row
        {
            public int RowNumber;
            public string? Code;
            public string LastName = "";
            public string FirstName = "";
            public string Email = "";
            public string? Phone, Gender, Address, City, DepartmentCode, PositionCode, ManagerCode, EmploymentType;
            public DateOnly? DateOfBirth, JoinDate;
            public decimal? BaseSalary, InsuranceSalary;
            public int Dependents;
            public List<string> Errors = new();
        }

        public async Task<EmployeeImportResultDto> ImportAsync(Stream file, bool dryRun, int? createdBy)
        {
            List<Row> rows;
            try
            {
                rows = ReadRows(file);
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                throw new ArgumentException("Không đọc được file. Hãy dùng file mẫu .xlsx của hệ thống.");
            }

            if (rows.Count == 0)
                throw new ArgumentException("File không có dòng dữ liệu nào.");
            if (rows.Count > MaxRows)
                throw new ArgumentException($"Mỗi lần chỉ nhập được tối đa {MaxRows} nhân viên.");

            var departments = await _context.Departments.AsNoTracking()
                .ToDictionaryAsync(d => d.DepartmentCode, d => d.DepartmentId, StringComparer.OrdinalIgnoreCase);
            var positions = await _context.Positions.AsNoTracking()
                .ToDictionaryAsync(p => p.PositionCode, p => p.PositionId, StringComparer.OrdinalIgnoreCase);
            var existing = await _context.Employees.AsNoTracking()
                .Select(e => new { e.EmployeeId, e.EmployeeCode, e.Email })
                .ToListAsync();
            var existingCodes = existing.ToDictionary(e => e.EmployeeCode, e => e.EmployeeId, StringComparer.OrdinalIgnoreCase);
            var existingEmails = existing.Select(e => e.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var fileCodes = rows.Where(r => r.Code != null).GroupBy(r => r.Code!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var fileEmails = rows.Where(r => r.Email != "").GroupBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var today = DateOnly.FromDateTime(AppTime.Today);

            foreach (var row in rows)
            {
                if (row.LastName == "") row.Errors.Add("Thiếu họ và tên đệm.");
                if (row.FirstName == "") row.Errors.Add("Thiếu tên.");
                if (row.Email == "") row.Errors.Add("Thiếu email.");
                else if (!MailAddress.TryCreate(row.Email, out _)) row.Errors.Add($"Email '{row.Email}' không hợp lệ.");
                else if (existingEmails.Contains(row.Email)) row.Errors.Add($"Email '{row.Email}' đã có nhân viên khác dùng.");
                else if (fileEmails[row.Email] > 1) row.Errors.Add($"Email '{row.Email}' lặp lại trong file.");

                if (row.Code != null)
                {
                    if (existingCodes.ContainsKey(row.Code)) row.Errors.Add($"Mã NV '{row.Code}' đã tồn tại.");
                    else if (fileCodes[row.Code] > 1) row.Errors.Add($"Mã NV '{row.Code}' lặp lại trong file.");
                }

                if (row.JoinDate == null) row.Errors.Add("Thiếu hoặc sai ngày vào làm (dd/MM/yyyy).");
                if (row.DateOfBirth is { } dob)
                {
                    if (dob > today) row.Errors.Add("Ngày sinh ở tương lai.");
                    else if (row.JoinDate is { } join && join < dob.AddYears(18)) row.Errors.Add("Nhân viên chưa đủ 18 tuổi khi vào làm.");
                }

                if (row.DepartmentCode != null && !departments.ContainsKey(row.DepartmentCode))
                    row.Errors.Add($"Không có phòng ban mã '{row.DepartmentCode}'.");
                if (row.PositionCode != null && !positions.ContainsKey(row.PositionCode))
                    row.Errors.Add($"Không có chức vụ mã '{row.PositionCode}'.");
                if (row.ManagerCode != null && !existingCodes.ContainsKey(row.ManagerCode) && !fileCodes.ContainsKey(row.ManagerCode))
                    row.Errors.Add($"Không có nhân viên quản lý mã '{row.ManagerCode}' (trong hệ thống hoặc trong file).");
                if (row.ManagerCode != null && row.Code != null && string.Equals(row.ManagerCode, row.Code, StringComparison.OrdinalIgnoreCase))
                    row.Errors.Add("Nhân viên không thể tự quản lý chính mình.");

                if (row.EmploymentType != null && !EmploymentTypes.Contains(row.EmploymentType, StringComparer.OrdinalIgnoreCase))
                    row.Errors.Add($"Loại hình '{row.EmploymentType}' không hợp lệ (Full-Time, Part-Time, Contract, Intern).");
                if (row.BaseSalary < 0 || row.InsuranceSalary < 0) row.Errors.Add("Lương không được âm.");
                if (row.Dependents is < 0 or > 20) row.Errors.Add("Số người phụ thuộc phải từ 0 đến 20.");
            }

            var result = new EmployeeImportResultDto
            {
                DryRun = dryRun,
                TotalRows = rows.Count,
                ValidRows = rows.Count(r => r.Errors.Count == 0),
                Errors = rows.Where(r => r.Errors.Count > 0)
                    .Select(r => new EmployeeImportRowErrorDto { Row = r.RowNumber, EmployeeCode = r.Code, Email = r.Email, Messages = r.Errors })
                    .ToList()
            };
            if (dryRun || result.Errors.Count > 0)
                return result;

            // Codes left blank get the next free EMPnnn, like the single "add employee" form
            var used = existingCodes.Keys.Concat(fileCodes.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var next = 1;
            foreach (var row in rows.Where(r => r.Code == null))
            {
                while (used.Contains($"EMP{next:D3}")) next++;
                row.Code = $"EMP{next:D3}";
                used.Add(row.Code);
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            var created = new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var employee = new Employee
                {
                    EmployeeCode = row.Code!,
                    FirstName = row.FirstName,
                    LastName = row.LastName,
                    FullName = $"{row.FirstName} {row.LastName}",
                    Email = row.Email,
                    Phone = row.Phone,
                    Gender = row.Gender,
                    DateOfBirth = row.DateOfBirth,
                    Address = row.Address,
                    City = row.City,
                    DepartmentId = row.DepartmentCode != null ? departments[row.DepartmentCode] : null,
                    PositionId = row.PositionCode != null ? positions[row.PositionCode] : null,
                    JoinDate = row.JoinDate!.Value,
                    EmploymentStatus = "Active",
                    EmploymentType = EmploymentTypes.First(t => string.Equals(t, row.EmploymentType ?? "Full-Time", StringComparison.OrdinalIgnoreCase)),
                    BaseSalary = row.BaseSalary,
                    InsuranceSalary = row.InsuranceSalary,
                    NumberOfDependents = row.Dependents,
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy = createdBy
                };
                _context.Employees.Add(employee);
                created[employee.EmployeeCode] = employee;
            }
            await _context.SaveChangesAsync();

            // Managers can be other rows of the same file, so they are linked once everyone has an id
            foreach (var row in rows.Where(r => r.ManagerCode != null))
                created[row.Code!].ManagerId = existingCodes.TryGetValue(row.ManagerCode!, out var id) ? id : created[row.ManagerCode!].EmployeeId;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            result.Imported = rows.Count;
            return result;
        }

        private static List<Row> ReadRows(Stream file)
        {
            using var workbook = new XLWorkbook(file);
            var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name == SheetName) ?? workbook.Worksheet(1);
            var rows = new List<Row>();
            var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
            for (var r = 2; r <= last; r++)
            {
                var cells = Enumerable.Range(1, Headers.Length).Select(c => sheet.Cell(r, c)).ToArray();
                if (cells.All(c => string.IsNullOrWhiteSpace(c.GetFormattedString())))
                    continue;

                var row = new Row
                {
                    RowNumber = r,
                    Code = Text(cells[0]),
                    LastName = Text(cells[1]) ?? "",
                    FirstName = Text(cells[2]) ?? "",
                    Email = Text(cells[3]) ?? "",
                    Phone = Text(cells[4]),
                    DepartmentCode = Text(cells[7]),
                    PositionCode = Text(cells[8]),
                    ManagerCode = Text(cells[9]),
                    EmploymentType = Text(cells[11]),
                    Address = Text(cells[15]),
                    City = Text(cells[16]),
                };

                var gender = Text(cells[5]);
                if (gender != null)
                {
                    if (Genders.TryGetValue(gender, out var g)) row.Gender = g;
                    else row.Errors.Add($"Giới tính '{gender}' không hợp lệ (Nam, Nữ, Khác).");
                }

                row.DateOfBirth = Date(cells[6], "Ngày sinh", row.Errors);
                row.JoinDate = Date(cells[10], "Ngày vào làm", row.Errors);
                row.BaseSalary = Number(cells[12], "Lương cơ bản", row.Errors);
                row.InsuranceSalary = Number(cells[13], "Lương đóng BH", row.Errors);
                row.Dependents = (int)(Number(cells[14], "Số người phụ thuộc", row.Errors) ?? 0);
                rows.Add(row);
            }
            return rows;
        }

        private static string? Text(IXLCell cell)
        {
            var value = cell.GetFormattedString().Trim();
            return value.Length == 0 ? null : value;
        }

        // Excel stores a typed date as a number; a date typed as text is read as dd/MM/yyyy
        private static DateOnly? Date(IXLCell cell, string label, List<string> errors)
        {
            if (cell.IsEmpty()) return null;
            if (cell.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(cell.GetDateTime());
            var text = cell.GetFormattedString().Trim();
            if (DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
            errors.Add($"{label} '{text}' không đúng định dạng dd/MM/yyyy.");
            return null;
        }

        private static decimal? Number(IXLCell cell, string label, List<string> errors)
        {
            if (cell.IsEmpty()) return null;
            if (cell.DataType == XLDataType.Number) return (decimal)cell.GetDouble();
            var text = cell.GetFormattedString().Trim().Replace(".", "").Replace(",", "");
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                return value;
            errors.Add($"{label} '{cell.GetFormattedString()}' không phải là số.");
            return null;
        }
    }
}
