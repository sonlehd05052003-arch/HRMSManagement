using Microsoft.AspNetCore.Authorization;
using HRManagement.DTOs;
using HRManagement.Services.Positions;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PositionController : ControllerBase
    {
        private readonly IPositionService _positionService;
        public PositionController(IPositionService positionService)
        {
            _positionService = positionService;
        }
        [HttpPost]
        public async Task<ActionResult<PositionResponseDto>> CreatePosition([FromBody] CreatePositionDto createDto)
        {
            try
            {
                var position = await _positionService.CreatePositionAsync(createDto);
                return CreatedAtAction(
                    nameof(GetPositionById),
                    new { id = position.PositionId },
                    position);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<PositionListDto>>> GetAllPositions()
        {
            var positions = await _positionService.GetAllPositionsAsync();
            return Ok(positions);
        }

        [HttpGet("active")]
        public async Task<ActionResult<IEnumerable<PositionListDto>>> GetActivePositions()
        {
            var positions = await _positionService.GetActivePositionsAsync();
            return Ok(positions);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<PositionResponseDto>> GetPositionById(int id)
        {
            var position = await _positionService.GetPositionByIdAsync(id);

            if (position == null)
                return NotFound(new { message = "Position not found." });

            return Ok(position);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<PositionResponseDto>> UpdatePosition(
            int id,
            [FromBody] UpdatePositionDto updateDto)
        {
            try
            {
                var position = await _positionService.UpdatePositionAsync(id, updateDto);
                return Ok(position);
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
        public async Task<ActionResult> DeactivatePosition(int id)
        {
            try
            {
                var success = await _positionService.DeactivatePositionAsync(id);

                if (!success)
                    return NotFound(new { message = "Position not found or already inactive." });

                return Ok(new { message = "Position deactivated successfully." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPatch("{id}/activate")]
        public async Task<ActionResult> ActivatePosition(int id)
        {
            var success = await _positionService.ActivatePositionAsync(id);

            if (!success)
                return NotFound(new { message = "Position not found or already active." });

            return Ok(new { message = "Position activated successfully." });
        }
    }
}
