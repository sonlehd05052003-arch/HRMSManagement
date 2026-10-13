namespace HRManagement.Models;

/// <summary>
/// Dữ liệu thuộc về một công ty. HrmsDbContext tự lọc mọi truy vấn theo công ty hiện tại
/// và tự gán CompanyId khi thêm mới, nên service không cần tự xử lý.
/// </summary>
public interface ICompanyScoped
{
    int CompanyId { get; set; }
}

public partial class AttendanceLog : ICompanyScoped { public int CompanyId { get; set; } }
public partial class AttendanceRecord : ICompanyScoped { public int CompanyId { get; set; } }
public partial class AuditLog : ICompanyScoped { public int CompanyId { get; set; } }
public partial class Department : ICompanyScoped { public int CompanyId { get; set; } }
public partial class Employee : ICompanyScoped { public int CompanyId { get; set; } }
public partial class EmployeeContract : ICompanyScoped { public int CompanyId { get; set; } }
public partial class EmployeeDocument : ICompanyScoped { public int CompanyId { get; set; } }
public partial class FaceProfile : ICompanyScoped { public int CompanyId { get; set; } }
public partial class FaceVerificationLog : ICompanyScoped { public int CompanyId { get; set; } }
public partial class Holiday : ICompanyScoped { public int CompanyId { get; set; } }
public partial class Hrprocedure : ICompanyScoped { public int CompanyId { get; set; } }
public partial class LeaveBalance : ICompanyScoped { public int CompanyId { get; set; } }
public partial class LeaveRequest : ICompanyScoped { public int CompanyId { get; set; } }
public partial class LeaveType : ICompanyScoped { public int CompanyId { get; set; } }
public partial class Notification : ICompanyScoped { public int CompanyId { get; set; } }
public partial class OvertimeRequest : ICompanyScoped { public int CompanyId { get; set; } }
public partial class PayrollAllowance : ICompanyScoped { public int CompanyId { get; set; } }
public partial class PayrollDeduction : ICompanyScoped { public int CompanyId { get; set; } }
public partial class PayrollFeedback : ICompanyScoped { public int CompanyId { get; set; } }
public partial class PayrollPeriod : ICompanyScoped { public int CompanyId { get; set; } }
public partial class PayrollPolicy : ICompanyScoped { public int CompanyId { get; set; } }
public partial class PayrollRecord : ICompanyScoped { public int CompanyId { get; set; } }
public partial class Payslip : ICompanyScoped { public int CompanyId { get; set; } }
public partial class Position : ICompanyScoped { public int CompanyId { get; set; } }
public partial class ResignationRequest : ICompanyScoped { public int CompanyId { get; set; } }
public partial class Role : ICompanyScoped { public int CompanyId { get; set; } }
public partial class Shift : ICompanyScoped { public int CompanyId { get; set; } }
public partial class ShiftAssignment : ICompanyScoped { public int CompanyId { get; set; } }
public partial class SystemSetting : ICompanyScoped { public int CompanyId { get; set; } }
public partial class Task : ICompanyScoped { public int CompanyId { get; set; } }

/// <summary>
/// User có CompanyId null là tài khoản cấp hệ thống (SuperAdmin), không thuộc công ty nào.
/// </summary>
public partial class User
{
    public int? CompanyId { get; set; }

    public virtual Company? Company { get; set; }
}
