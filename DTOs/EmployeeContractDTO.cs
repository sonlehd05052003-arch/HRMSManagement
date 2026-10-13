using System.ComponentModel.DataAnnotations;

namespace HRManagement.DTOs
{
    public class EmployeeContractDto
    {
        public int ContractId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = null!;
        public string ContractNumber { get; set; } = null!;
        /// <summary>Probation, FixedTerm or Indefinite</summary>
        public string ContractType { get; set; } = null!;
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public decimal SalaryAmount { get; set; }
        public string? Terms { get; set; }
        /// <summary>Active, Terminated, or Expired once an active contract is past its end date</summary>
        public string Status { get; set; } = null!;
        /// <summary>Days until the end date for an active contract, negative once past it; null without an end date</summary>
        public int? DaysRemaining { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class EmployeeContractWriteDto
    {
        [Required]
        public int EmployeeId { get; set; }

        /// <summary>Left empty, a number such as HD-2026-0001 is generated</summary>
        [MaxLength(50)]
        public string? ContractNumber { get; set; }

        [Required]
        [MaxLength(50)]
        public string ContractType { get; set; } = null!;

        [Required]
        public DateOnly StartDate { get; set; }

        public DateOnly? EndDate { get; set; }

        [Range(0, 10_000_000_000)]
        public decimal SalaryAmount { get; set; }

        public string? Terms { get; set; }
    }
}
