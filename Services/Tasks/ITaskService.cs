using HRManagement.DTOs;

namespace HRManagement.Services.Tasks
{
    public interface ITaskService
    {
        System.Threading.Tasks.Task<IEnumerable<TaskDTO>> GetAllAsync();
        System.Threading.Tasks.Task<TaskDTO?> GetByIdAsync(int id);
        System.Threading.Tasks.Task<(bool Success, string? Error, TaskDTO? Data)> CreateAsync(int currentUserId, CreateTaskDTO dto);
        System.Threading.Tasks.Task<(bool Success, string? Error, bool NotFound, bool Forbidden)> UpdateAsync(int id, int currentUserId, UpdateTaskDTO dto);
        System.Threading.Tasks.Task<(bool Success, string? Error, bool NotFound, bool Forbidden)> ApproveAsync(int id, int currentUserId, ApproveTaskDTO dto);
        System.Threading.Tasks.Task<(bool Success, string? Error, bool NotFound, bool Forbidden)> RejectAsync(int id, int currentUserId, RejectTaskDTO dto);
        System.Threading.Tasks.Task<(bool Success, string? Error, bool NotFound, bool Forbidden)> CancelAsync(int id, int currentUserId);
    }
}
