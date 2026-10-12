using HRManagement.DTOs.LeaveTypes;
using HRManagement.Services.Leaves;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Controllers
{
    [ApiController]
    [Route("api/leave-types")]
    [Authorize]
    public class LeaveTypesController : ControllerBase
    {
        private readonly ILeaveTypeService _leaveTypeService;

        public LeaveTypesController(ILeaveTypeService leaveTypeService)
        {
            _leaveTypeService = leaveTypeService;
        }

        [HttpGet]
        public async Task<IActionResult> GetActiveLeaveTypes()
        {
            var result = await _leaveTypeService.GetActiveLeaveTypesAsync();
            return Ok(result);
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAllLeaveTypes()
        {
            var result = await _leaveTypeService.GetAllLeaveTypesAsync();
            return Ok(result);
        }

        // GET: api/leave-types/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetLeaveTypeById(int id)
        {
            var result = await _leaveTypeService.GetLeaveTypeByIdAsync(id);

            if (result == null)
                return NotFound(new { message = "Leave type not found." });

            return Ok(result);
        }

        // POST: api/leave-types
        [HttpPost]
        public async Task<IActionResult> CreateLeaveType([FromBody] CreateLeaveTypeDTO dto)
        {
            var result = await _leaveTypeService.CreateLeaveTypeAsync(dto);
            return CreatedAtAction(nameof(GetLeaveTypeById), new { id = result.LeaveTypeId }, result);
        }

        // PUT: api/leave-types/5
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateLeaveType(int id, [FromBody] UpdateLeaveTypeDTO dto)
        {
            var result = await _leaveTypeService.UpdateLeaveTypeAsync(id, dto);

            if (result == null)
                return NotFound(new { message = "Leave type not found." });

            return Ok(result);
        }
    }
}