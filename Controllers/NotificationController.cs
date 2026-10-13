using HRManagement.Services.CurrentUsers;
using HRManagement.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Controllers
{
    /// <summary>The signed-in user's own notifications; nobody can read or mark anyone else's.</summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _notifications;
        private readonly ICurrentUserService _currentUser;

        public NotificationController(INotificationService notifications, ICurrentUserService currentUser)
        {
            _notifications = notifications;
            _currentUser = currentUser;
        }

        [HttpGet]
        public async Task<IActionResult> GetMine([FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            if (page < 1 || pageSize < 1 || pageSize > 100)
                return BadRequest(new { message = "Trang không hợp lệ (pageSize tối đa 100)." });

            return Ok(await _notifications.GetMineAsync(_currentUser.GetCurrentUserId(), unreadOnly, page, pageSize));
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> UnreadCount() =>
            Ok(new { count = await _notifications.CountUnreadAsync(_currentUser.GetCurrentUserId()) });

        [HttpPut("{id:int}/read")]
        public async Task<IActionResult> MarkRead(int id) =>
            await _notifications.MarkReadAsync(_currentUser.GetCurrentUserId(), id)
                ? NoContent()
                : NotFound(new { message = "Không tìm thấy thông báo." });

        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllRead() =>
            Ok(new { updated = await _notifications.MarkAllReadAsync(_currentUser.GetCurrentUserId()) });
    }
}
