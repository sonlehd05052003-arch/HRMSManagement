using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HRManagement.Models;

public partial class PayrollFeedback
{
    [Key]
    public int FeedbackId { get; set; }

    [Required]
    public int PayrollRecordId { get; set; }

    [Required]
    public int EmployeeId { get; set; }

    [MaxLength(500)]
    public string? Content { get; set; }

    public bool IsAgreed { get; set; } = false;

    public DateTime SubmittedAt { get; set; } = AppTime.Now;

    // "Pending" | "Resolved" | "Dismissed"
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    [MaxLength(500)]
    public string? HrResponse { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public int? ResolvedByUserId { get; set; }

    [ForeignKey("PayrollRecordId")]
    public virtual PayrollRecord PayrollRecord { get; set; } = null!;

    [ForeignKey("EmployeeId")]
    public virtual Employee Employee { get; set; } = null!;

    [ForeignKey("ResolvedByUserId")]
    public virtual User? ResolvedByUser { get; set; }
}
