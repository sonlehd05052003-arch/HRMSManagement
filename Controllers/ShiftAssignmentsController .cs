using HRManagement.DataAcess;
using HRManagement.DTOs.ShiftAssignments;
using HRManagement.Services.Attendances;
using HRManagement.Services.CurrentUsers;
using HRManagement.Services.Shifts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShiftAssignmentsController : Controller
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly IShiftAssignmentService _shiftAssignmentService;
        public ShiftAssignmentsController(ICurrentUserService currentUserService, IShiftAssignmentService shiftAssignmentService)
        {
            _currentUserService = currentUserService;
            _shiftAssignmentService = shiftAssignmentService;
        }

        [HttpPost("assign")]
        [Authorize]
        public async Task<IActionResult> AssignShift([FromBody] AssignShiftDto dto)
        {
            try
            {
                int managerId = _currentUserService.GetCurrentUserId();

                await _shiftAssignmentService.AssignShiftAsync(managerId, dto);

                return Ok(new
                {
                    success = true,
                    message = "Phân ca thành công."
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetShiftAssignments(
            [FromQuery] DateOnly? date,
            [FromQuery] int? employeeId,
            [FromQuery] string? status)
        {
            var data = await _shiftAssignmentService.GetShiftAssignmentsAsync(date, employeeId, status);

            return Ok(new
            {
                success = true,
                data
            });
        }

        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetShiftAssignmentById(int id)
        {
            var data = await _shiftAssignmentService.GetShiftAssignmentByIdAsync(id);

            if (data == null)
                return NotFound(new { success = false, message = "Không tìm thấy phân ca." });

            return Ok(new
            {
                success = true,
                data
            });
        }

        [HttpGet("my-schedule")]
        [Authorize]
        public async Task<IActionResult> GetMySchedule(
            [FromQuery] DateOnly? fromDate,
            [FromQuery] DateOnly? toDate)
        {
            // ⚠ nếu currentUserId != employeeId thì bạn nên đổi sang GetCurrentEmployeeIdAsync()
            var employeeId = await _currentUserService.GetCurrentEmployeeIdAsync();

            var data = await _shiftAssignmentService.GetMyShiftAssignmentsAsync(employeeId, fromDate, toDate);

            return Ok(new
            {
                success = true,
                data
            });
        }

        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> UpdateShiftAssignment(int id, [FromBody] UpdateShiftAssignmentDto dto)
        {
            try
            {
                var result = await _shiftAssignmentService.UpdateShiftAssignmentAsync(id, dto);

                return Ok(new
                {
                    success = true,
                    message = "Cập nhật phân ca thành công.",
                    data = result
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
        }

        [HttpPatch("{id}/deactivate")]
        [Authorize]
        public async Task<IActionResult> DeactivateShiftAssignment(int id)
        {
            try
            {
                await _shiftAssignmentService.DeactivateShiftAssignmentAsync(id);

                return Ok(new
                {
                    success = true,
                    message = "Hủy / vô hiệu hóa phân ca thành công."
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
        }

        [HttpPatch("{id}/activate")]
        [Authorize]
        public async Task<IActionResult> ActivateShiftAssignment(int id)
        {
            try
            {
                await _shiftAssignmentService.ActivateShiftAssignmentAsync(id);

                return Ok(new
                {
                    success = true,
                    message = "Kích hoạt lại phân ca thành công."
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteShiftAssignment(int id)
        {
            try
            {
                await _shiftAssignmentService.DeleteShiftAssignmentAsync(id);

                return Ok(new
                {
                    success = true,
                    message = "Xóa phân ca thành công."
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
        }
    }
}
