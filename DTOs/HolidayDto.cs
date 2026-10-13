using System.ComponentModel.DataAnnotations;

namespace HRManagement.DTOs
{
    public class HolidayDto
    {
        public int HolidayId { get; set; }
        public DateOnly HolidayDate { get; set; }
        public string Name { get; set; } = null!;
        public bool IsPaid { get; set; }
    }

    public class HolidayWriteDto
    {
        [Required]
        public DateOnly HolidayDate { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = null!;

        public bool IsPaid { get; set; } = true;
    }
}
