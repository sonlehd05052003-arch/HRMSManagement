namespace HRManagement.DTOs
{
    public class ReportCountDto
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }

    public class WorkforceMonthDto
    {
        public int Month { get; set; }
        /// <summary>Employees on the payroll on the last day of the month (today for the current month)</summary>
        public int Headcount { get; set; }
        public int Hires { get; set; }
        public int Leavers { get; set; }
    }

    public class WorkforceReportDto
    {
        public int Year { get; set; }
        /// <summary>The day the breakdowns below are taken at: today, or 31/12 for a past year</summary>
        public DateOnly AsOf { get; set; }
        public int Headcount { get; set; }
        public int Hires { get; set; }
        public int Leavers { get; set; }
        /// <summary>Leavers over the average monthly headcount, in percent</summary>
        public decimal TurnoverRate { get; set; }
        public List<WorkforceMonthDto> Months { get; set; } = new();
        public List<ReportCountDto> ByDepartment { get; set; } = new();
        public List<ReportCountDto> ByGender { get; set; } = new();
        public List<ReportCountDto> ByAge { get; set; } = new();
        public List<ReportCountDto> ByTenure { get; set; } = new();
        public List<ReportCountDto> ByEmploymentType { get; set; } = new();
    }

    public class AttendanceReportRowDto
    {
        public int? DepartmentId { get; set; }
        public string Department { get; set; } = "";
        public int Employees { get; set; }
        /// <summary>Past shift days, holidays left out</summary>
        public int ScheduledDays { get; set; }
        public int WorkedDays { get; set; }
        public int LateDays { get; set; }
        public int LeaveDays { get; set; }
        public int AbsentDays { get; set; }
        public decimal OvertimeHours { get; set; }
        /// <summary>Worked days over scheduled days, in percent</summary>
        public decimal AttendanceRate { get; set; }
        /// <summary>Late days over worked days, in percent</summary>
        public decimal LateRate { get; set; }
    }

    public class AttendanceReportDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        public AttendanceReportRowDto Total { get; set; } = new();
        public List<AttendanceReportRowDto> Departments { get; set; } = new();
    }

    public class PayrollReportRowDto
    {
        public int? Month { get; set; }
        public int? DepartmentId { get; set; }
        public string Label { get; set; } = "";
        public string? Status { get; set; }
        public int Employees { get; set; }
        public decimal BaseSalary { get; set; }
        public decimal Allowances { get; set; }
        public decimal OvertimePay { get; set; }
        public decimal Bonus { get; set; }
        public decimal Gross { get; set; }
        public decimal Insurance { get; set; }
        public decimal Tax { get; set; }
        public decimal Deductions { get; set; }
        public decimal Net { get; set; }
    }

    public class PayrollReportDto
    {
        public int Year { get; set; }
        /// <summary>The month the department split is for; null when the year has no payroll yet</summary>
        public int? Month { get; set; }
        public PayrollReportRowDto Total { get; set; } = new();
        public List<PayrollReportRowDto> Months { get; set; } = new();
        public List<PayrollReportRowDto> Departments { get; set; } = new();
    }
}
