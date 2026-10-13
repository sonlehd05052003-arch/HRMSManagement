using HRManagement.Authorization;
using HRManagement.DTOs;
using HRManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Controllers
{
    /// <summary>
    /// Read-only view of AuditLogs for the company's administrators: who changed pay, attendance, leave balances,
    /// employees, roles and settings, when, from where, and the values before and after.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequirePermission("AuditLog.View")]
    public class AuditLogController : ControllerBase
    {
        private readonly HrmsDbContext _context;

        public AuditLogController(HrmsDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Search(
            [FromQuery] string? tableName,
            [FromQuery] string? action,
            [FromQuery] int? userId,
            [FromQuery] int? recordId,
            [FromQuery] DateOnly? fromDate,
            [FromQuery] DateOnly? toDate,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (page < 1 || pageSize < 1 || pageSize > 100)
                return BadRequest(new { message = "Trang không hợp lệ (pageSize tối đa 100)." });
            if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
                return BadRequest(new { message = "Từ ngày không được lớn hơn đến ngày." });

            var query = _context.AuditLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(tableName))
                query = query.Where(a => a.TableName == tableName);
            if (!string.IsNullOrWhiteSpace(action))
                query = query.Where(a => a.Action == action);
            if (userId.HasValue)
                query = query.Where(a => a.UserId == userId);
            if (recordId.HasValue)
                query = query.Where(a => a.RecordId == recordId);
            if (fromDate.HasValue)
                query = query.Where(a => a.ActionDate >= fromDate.Value.ToDateTime(TimeOnly.MinValue));
            if (toDate.HasValue)
                query = query.Where(a => a.ActionDate < toDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var result = new PagedResultDto<AuditLogDto>
            {
                Page = page,
                PageSize = pageSize,
                Total = await query.CountAsync(),
                Items = await query
                    .OrderByDescending(a => a.ActionDate).ThenByDescending(a => a.AuditId)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(a => new AuditLogDto
                    {
                        AuditId = a.AuditId,
                        TableName = a.TableName,
                        Action = a.Action,
                        RecordId = a.RecordId,
                        UserId = a.UserId,
                        UserName = a.User != null ? a.User.Username : null,
                        OldValues = a.OldValues,
                        NewValues = a.NewValues,
                        ActionDate = a.ActionDate,
                        IpAddress = a.Ipaddress
                    })
                    .ToListAsync()
            };
            return Ok(result);
        }

        /// <summary>The tables that have entries, for the filter.</summary>
        [HttpGet("tables")]
        public async Task<IActionResult> Tables() =>
            Ok(await _context.AuditLogs.AsNoTracking().Select(a => a.TableName).Distinct().OrderBy(t => t).ToListAsync());
    }
}
