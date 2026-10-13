using HRManagement.Authorization;
using HRManagement.DTOs;
using HRManagement.Services.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EmployeeController : ControllerBase
    {
        private readonly IEmployeeService _employeeService;
        private readonly IEmployeeImportService _importService;
        public EmployeeController(IEmployeeService employeeService, IEmployeeImportService importService)
        {
            _employeeService = employeeService;
            _importService = importService;
        }

        [HttpGet("import-template")]
        [RequirePermission("Employee.Manage")]
        public async Task<IActionResult> GetImportTemplate()
        {
            var bytes = await _importService.BuildTemplateAsync();
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "mau-nhap-nhan-vien.xlsx");
        }

        /// <summary>Checks an Excel file of employees (dryRun) or adds them all; nothing is added while any row has an error.</summary>
        [HttpPost("import")]
        [RequirePermission("Employee.Manage")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> Import(IFormFile file, [FromQuery] bool dryRun = true)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Chưa chọn file." });
            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Chỉ nhận file .xlsx." });

            var userId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : (int?)null;
            try
            {
                await using var stream = file.OpenReadStream();
                return Ok(await _importService.ImportAsync(stream, dryRun, userId));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetAllEmployees()
        {
            var employees = await _employeeService.GetAllEmployeesAsync();
            return Ok(employees);
        }

        [HttpGet("active")]
        public async Task<IActionResult> GetActiveEmployees()
        {
            var employees = await _employeeService.GetActiveEmployeesAsync();
            return Ok(employees);
        }

        [HttpGet("approval-analysis")]
        [RequirePermission("Employee.Manage")]
        public async Task<IActionResult> GetApprovalAnalysis()
        {
            var analysis = await _employeeService.GetApprovalAnalysisAsync();
            return Ok(analysis);
        }
        [HttpPost]
        [RequirePermission("Employee.Manage")]
        public async Task<IActionResult> Create([FromBody] CreateEmployeeDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var created = await _employeeService.AddEmployeeAsync(dto);

                return CreatedAtAction(
                    nameof(GetById),
                    new { id = created.EmployeeId },
                    created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }
        [HttpPut("{id}")]
        [RequirePermission("Employee.Manage")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateEmployeeDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var updated = await _employeeService.UpdateEmployeeAsync(id, dto);
                return Ok(updated);
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { message = $"Employee {id} not found." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }
        [HttpPatch("{id}/status")]
        [RequirePermission("Employee.Manage")]
        public async Task<IActionResult> UpdateStatus(
            int id,
            [FromQuery] string status,
            [FromQuery] int? modifiedBy = null)
        {
            try
            {
                var result = await _employeeService.UpdateStatusAsync(id, status, modifiedBy);

                if (!result)
                    return NotFound(new { message = $"Employee {id} not found." });

                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var employee = await _employeeService.GetEmployeeByIdAsync(id);
            if (employee is null) return NotFound(new { message = $"Employee {id} not found." });
            return Ok(employee);
        }
    }
}
