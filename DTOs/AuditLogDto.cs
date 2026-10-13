namespace HRManagement.DTOs
{
    public class AuditLogDto
    {
        public int AuditId { get; set; }
        public string TableName { get; set; } = null!;
        /// <summary>INSERT, UPDATE, DELETE (or SELECT for exports)</summary>
        public string Action { get; set; } = null!;
        public int RecordId { get; set; }
        public int? UserId { get; set; }
        public string? UserName { get; set; }
        /// <summary>JSON object of the changed fields before the change; null for an insert</summary>
        public string? OldValues { get; set; }
        /// <summary>JSON object of the changed fields after the change; null for a delete</summary>
        public string? NewValues { get; set; }
        public DateTime ActionDate { get; set; }
        public string? IpAddress { get; set; }
    }
}
