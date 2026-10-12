using HRManagement.DTOs;
using HRManagement.Models;
using HRManagement.Services.CurrentUsers;
using HRManagement.Services.Emails;
using HRManagement.Services.Audits;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace HRManagement.Services.Exports
{
    public class ExportService : IExportService
    {
        private readonly HrmsDbContext _context;
        private readonly ICurrentUserService _currentUserService;
        private readonly IAuditService _auditService;
        private readonly IEmailService _emailService;

        public ExportService(
            HrmsDbContext context,
            ICurrentUserService currentUserService,
            IAuditService auditService,
            IEmailService emailService)
        {
            _context = context;
            _currentUserService = currentUserService;
            _auditService = auditService;
            _emailService = emailService;
        }

        public async Task<ExportResponseDTO> ExportAsync(ExportRequestDTO request)
        {
            int userId = _currentUserService.GetUserId();

            var data = await GetDataAsync(request);

            if (data.Count == 0)
            {
                return new ExportResponseDTO
                {
                    Success = false,
                    Message = "MSG-83: No data to export."
                };
            }

            byte[] fileBytes;
            string contentType;
            string extension;

            switch (request.Format.ToLower())
            {
                case "csv":
                    fileBytes = GenerateCsv(data);
                    contentType = "text/csv";
                    extension = "csv";
                    break;

                case "excel":
                    fileBytes = GenerateCsv(data); // fallback nếu chưa cài ClosedXML
                    contentType = "application/vnd.ms-excel";
                    extension = "xls";
                    break;

                default:
                    return new ExportResponseDTO
                    {
                        Success = false,
                        Message = "MSG-81: Unsupported export format."
                    };
            }

            var fileName = $"{request.Module}_export_{AppTime.Now:yyyyMMddHHmmss}.{extension}";

            // Save audit log
            await _auditService.TrackAsync(userId, "SELECT", $"Export {request.Module}");

            // Send email if requested
            if (request.SendToEmail && !string.IsNullOrEmpty(request.EmailAddress))
            {
                await _emailService.SendAsync(
                    request.EmailAddress,
                    "Export Report",
                    $"Your report {fileName} has been generated."
                );
            }

            return new ExportResponseDTO
            {
                Success = true,
                FileBytes = fileBytes,
                FileName = fileName,
                ContentType = contentType,
                Message = "Export generated successfully"
            };
        }

        private async Task<List<Dictionary<string, object>>> GetDataAsync(ExportRequestDTO request)
        {
            switch (request.Module.ToLower())
            {
                case "attendance":
                    return await _context.AttendanceRecords
                        .Include(a => a.Employee)
                        .Select(a => new Dictionary<string, object>
                        {
                            {"Employee", a.Employee.FullName},
                            {"Date", a.AttendanceDate},
                            {"Status", a.Status},
                            {"WorkingHours", a.WorkingHours}
                        }).ToListAsync();

                case "leave":
                    return await _context.LeaveRequests
                        .Include(l => l.Employee)
                        .Select(l => new Dictionary<string, object>
                        {
                            {"Employee", l.Employee.FullName},
                            {"StartDate", l.StartDate},
                            {"EndDate", l.EndDate},
                            {"Status", l.Status}
                        }).ToListAsync();

                case "payroll":
                    return await _context.PayrollRecords
                        .Include(p => p.Employee)
                        .Select(p => new Dictionary<string, object>
                        {
                            {"Employee", p.Employee.FullName},
                            {"Salary", p.BaseSalary},
                            {"NetPay", p.NetPay}
                        }).ToListAsync();

                default:
                    return new List<Dictionary<string, object>>();
            }
        }

        private byte[] GenerateCsv(List<Dictionary<string, object>> data)
        {
            var sb = new StringBuilder();

            var headers = data.First().Keys.ToList();

            sb.AppendLine(string.Join(",", headers));

            foreach (var row in data)
            {
                sb.AppendLine(string.Join(",", row.Values));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }
}
