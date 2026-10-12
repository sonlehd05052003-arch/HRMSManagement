using HRManagement.DTOs;
using HRManagement.Authorization;
using HRManagement.Services.Departments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class DepartmentController : ControllerBase
    {
        private readonly IDepartmentService _departmentService;

        public DepartmentController(IDepartmentService departmentService)
        {
            _departmentService = departmentService;
        }

        [HttpPost]
        [RequirePermission("Employee.Manage")]
        public async Task<ActionResult<DepartmentResponseDto>> CreateDepartment([FromBody] CreateDepartmentDto createDto)
        {
            try
            {
                var department = await _departmentService.CreateDepartmentAsync(createDto);
                return CreatedAtAction(
                    nameof(GetDepartmentById),
                    new { id = department.DepartmentId },
                    department);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<DepartmentListDto>>> GetAllDepartments()
        {
            var departments = await _departmentService.GetAllDepartmentsAsync();
            return Ok(departments);
        }

        [HttpGet("active")]
        public async Task<ActionResult<IEnumerable<DepartmentListDto>>> GetActiveDepartments()
        {
            var departments = await _departmentService.GetActiveDepartmentsAsync();
            return Ok(departments);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<DepartmentResponseDto>> GetDepartmentById(int id)
        {
            var department = await _departmentService.GetDepartmentByIdAsync(id);

            if (department == null)
                return NotFound(new { message = "Department not found." });

            return Ok(department);
        }

        [HttpPut("{id}")]
        [RequirePermission("Employee.Manage")]
        public async Task<ActionResult<DepartmentResponseDto>> UpdateDepartment(
            int id,
            [FromBody] UpdateDepartmentDto updateDto)
        {
            try
            {
                var department = await _departmentService.UpdateDepartmentAsync(id, updateDto);
                return Ok(department);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPatch("{id}/deactivate")]
        [RequirePermission("Employee.Manage")]
        public async Task<ActionResult> DeactivateDepartment(int id)
        {
            try
            {
                var success = await _departmentService.DeactivateDepartmentAsync(id);

                if (!success)
                    return NotFound(new { message = "Department not found or already inactive." });

                return Ok(new { message = "Department deactivated successfully." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPatch("{id}/activate")]
        [RequirePermission("Employee.Manage")]
        public async Task<ActionResult> ActivateDepartment(int id)
        {
            var success = await _departmentService.ActivateDepartmentAsync(id);

            if (!success)
                return NotFound(new { message = "Department not found or already active." });

            return Ok(new { message = "Department activated successfully." });
        }

    }
}
