using System.ComponentModel.DataAnnotations;

namespace HRManagement.DTOs.LeaveBalance
{
    public class AdjustLeaveBalanceDTO
    {
        [Required]
        public int EmployeeId { get; set; }

        [Required]
        public int LeaveTypeId { get; set; }

        [Required]
        public string AdjustmentType { get; set; } = string.Empty;

        [Required]
        [Range(0.1, 365)]
        public decimal NumberOfDays { get; set; }

        [Required]
        public string Reason { get; set; } = string.Empty;

        public DateTime EffectiveDate { get; set; } = AppTime.Now;
    }
}