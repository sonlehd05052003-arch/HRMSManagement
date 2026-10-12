using Microsoft.AspNetCore.Authorization;
using HRManagement.DTOs;
using HRManagement.Services.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HRManagement.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class TaskController : ControllerBase
    {
        private readonly ITaskService _taskService;

        public TaskController(ITaskService taskService)
        {
            _taskService = taskService;
        }

        [HttpGet]
        public async System.Threading.Tasks.Task<IActionResult> GetAll()
        {
            var tasks = await _taskService.GetAllAsync();
            return Ok(tasks);
        }

        [HttpGet("{id}")]
        public async System.Threading.Tasks.Task<IActionResult> GetById(int id)
        {
            var task = await _taskService.GetByIdAsync(id);
            return task is null ? NotFound("Task not found") : Ok(task);
        }

        [HttpPost]
        public async System.Threading.Tasks.Task<IActionResult> Create(CreateTaskDTO dto)
        {
            var (success, error, data) = await _taskService.CreateAsync(GetCurrentUserId(), dto);
            return success ? Ok(data) : BadRequest(error);
        }

        [HttpPut("{id}")]
        public async System.Threading.Tasks.Task<IActionResult> Update(int id, UpdateTaskDTO dto)
        {
            var (success, error, notFound) = await _taskService.UpdateAsync(id, dto);
            if (notFound) return NotFound("Task not found");
            if (!success) return BadRequest(error);
            return Ok("Task updated successfully");
        }

        [HttpPatch("{id}/approve")]
        public async System.Threading.Tasks.Task<IActionResult> Approve(int id, ApproveTaskDTO dto)
        {
            var (success, error, notFound, forbidden) = await _taskService.ApproveAsync(id, GetCurrentUserId(), dto);
            if (notFound) return NotFound("Task not found");
            if (forbidden) return Forbid(error!);
            if (!success) return BadRequest(error);
            return Ok("Task approved successfully");
        }

        [HttpPatch("{id}/reject")]
        public async System.Threading.Tasks.Task<IActionResult> Reject(int id, RejectTaskDTO dto)
        {
            var (success, error, notFound, forbidden) = await _taskService.RejectAsync(id, GetCurrentUserId(), dto);
            if (notFound) return NotFound("Task not found");
            if (forbidden) return Forbid(error!);
            if (!success) return BadRequest(error);
            return Ok("Task rejected successfully");
        }

        [HttpDelete("{id}")]
        public async System.Threading.Tasks.Task<IActionResult> Cancel(int id)
        {
            var (success, error, notFound, forbidden) = await _taskService.CancelAsync(id, GetCurrentUserId());
            if (notFound) return NotFound("Task not found");
            if (forbidden) return Forbid(error!);
            if (!success) return BadRequest(error);
            return Ok("Task cancelled successfully");
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("User ID not found in token");
            return int.Parse(claim.Value);
        }
    }
}
