namespace HRManagement.Models;

/// <summary>
/// A day off for the whole company: a public holiday or a company day off. A paid holiday counts as a paid working
/// day in payroll, overtime on it uses the holiday multiplier, and nobody is marked absent on it.
/// </summary>
public partial class Holiday
{
    public int HolidayId { get; set; }

    public DateOnly HolidayDate { get; set; }

    public string Name { get; set; } = null!;

    public bool IsPaid { get; set; }

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }
}
