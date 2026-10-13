namespace HRManagement.DTOs
{
    public class UserNotificationDto
    {
        public int NotificationId { get; set; }
        public string NotificationType { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string Message { get; set; } = null!;
        public string? RelatedEntity { get; set; }
        public int? RelatedEntityId { get; set; }
        public bool IsRead { get; set; }
        public DateTime SentDate { get; set; }
    }
}
