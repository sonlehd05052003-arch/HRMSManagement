using AutoMapper;
using HRManagement.DataAcess.Interfaces;
using HRManagement.DTOs;
using Task = HRManagement.Models.Task;

namespace HRManagement.Services.Tasks
{
    public class TaskService : ITaskService
    {
        private readonly ITaskRepository _taskRepository;
        private readonly IMapper _mapper;

        public TaskService(ITaskRepository taskRepository, IMapper mapper)
        {
            _taskRepository = taskRepository;
            _mapper = mapper;
        }

        public async System.Threading.Tasks.Task<IEnumerable<TaskDTO>> GetAllAsync()
        {
            var tasks = await _taskRepository.GetAllAsync();
            return _mapper.Map<IEnumerable<TaskDTO>>(tasks);
        }

        public async System.Threading.Tasks.Task<TaskDTO?> GetByIdAsync(int id)
        {
            var task = await _taskRepository.GetByIdAsync(id);
            return task is null ? null : _mapper.Map<TaskDTO>(task);
        }

        public async System.Threading.Tasks.Task<(bool Success, string? Error, TaskDTO? Data)> CreateAsync(int currentUserId, CreateTaskDTO dto)
        {
            if (!await _taskRepository.UserExistsAsync(dto.AssignedTo))
                return (false, "Assigned user does not exist", null);

            var task = _mapper.Map<Task>(dto);
            task.Status = "Pending";
            task.CreatedDate = DateTime.UtcNow;
            task.CreatedBy = currentUserId;
            task.CompletedDate = null;

            await _taskRepository.AddAsync(task);

            var result = await _taskRepository.GetByIdAsync(task.TaskId);
            return (true, null, _mapper.Map<TaskDTO>(result));
        }

        public async System.Threading.Tasks.Task<(bool Success, string? Error, bool NotFound, bool Forbidden)> UpdateAsync(int id, int currentUserId, UpdateTaskDTO dto)
        {
            var task = await _taskRepository.FindAsync(id);
            if (task is null)
                return (false, null, true, false);

            if (task.CreatedBy != currentUserId)
                return (false, "You are not allowed to edit this task", false, true);

            if (dto.AssignedTo.HasValue && !await _taskRepository.UserExistsAsync(dto.AssignedTo.Value))
                return (false, "Assigned user does not exist", false, false);

            if (task.Status != "Pending")
                return (false, "Only pending tasks can be edited", false, false);

            _mapper.Map(dto, task);
            await _taskRepository.SaveChangesAsync();

            return (true, null, false, false);
        }

        public async System.Threading.Tasks.Task<(bool Success, string? Error, bool NotFound, bool Forbidden)> ApproveAsync(int id, int currentUserId, ApproveTaskDTO dto)
        {
            var task = await _taskRepository.FindAsync(id);
            if (task is null)
                return (false, null, true, false);

            if (task.AssignedTo != currentUserId)
                return (false, "You are not allowed to approve this task", false, true);

            if (task.Status != "Pending" && task.Status != "InProgress")
                return (false, "Task is not in approvable state", false, false);

            task.Status = "Approved";
            task.CompletedDate = DateTime.UtcNow;
            task.CompletionNotes = dto.Comments;
            await _taskRepository.SaveChangesAsync();

            return (true, null, false, false);
        }

        public async System.Threading.Tasks.Task<(bool Success, string? Error, bool NotFound, bool Forbidden)> RejectAsync(int id, int currentUserId, RejectTaskDTO dto)
        {
            var task = await _taskRepository.FindAsync(id);
            if (task is null)
                return (false, null, true, false);

            if (task.AssignedTo != currentUserId)
                return (false, "You are not allowed to reject this task", false, true);

            if (task.Status != "Pending" && task.Status != "InProgress")
                return (false, "Task is not in rejectable state", false, false);

            if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Length < 10)
                return (false, "Rejection reason must be at least 10 characters", false, false);

            task.Status = "Rejected";
            task.CompletedDate = DateTime.UtcNow;
            task.CompletionNotes = dto.Reason;
            await _taskRepository.SaveChangesAsync();

            return (true, null, false, false);
        }

        public async System.Threading.Tasks.Task<(bool Success, string? Error, bool NotFound, bool Forbidden)> CancelAsync(int id, int currentUserId)
        {
            var task = await _taskRepository.FindAsync(id);
            if (task is null)
                return (false, null, true, false);

            if (task.Status == "Approved" || task.Status == "Rejected")
                return (false, "Processed tasks cannot be cancelled", false, false);

            if (task.AssignedTo != currentUserId)
                return (false, "You are not allowed to cancel this task", false, true);

            task.Status = "Cancelled";
            task.CompletedDate = DateTime.UtcNow;
            await _taskRepository.SaveChangesAsync();

            return (true, null, false, false);
        }
    }
}
