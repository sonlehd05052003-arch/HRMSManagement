using HRManagement.Services.Tenants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;

namespace HRManagement.Models;

public partial class HrmsDbContext : DbContext
{
    private readonly ITenantContext? _tenant;

    public HrmsDbContext()
    {
    }

    [ActivatorUtilitiesConstructor]
    public HrmsDbContext(DbContextOptions<HrmsDbContext> options, ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    /// <summary>
    /// Công ty hiện tại; global query filter đọc giá trị này mỗi lần truy vấn.
    /// Null thì mọi bảng thuộc công ty đều trả về rỗng.
    /// </summary>
    public int? CurrentCompanyId => _tenant?.CompanyId;

    public virtual DbSet<Company> Companies { get; set; }

    public virtual DbSet<AttendanceRecord> AttendanceRecords { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Department> Departments { get; set; }

    public virtual DbSet<Employee> Employees { get; set; }

    public virtual DbSet<EmployeeContract> EmployeeContracts { get; set; }

    public virtual DbSet<EmployeeDocument> EmployeeDocuments { get; set; }

    public virtual DbSet<Holiday> Holidays { get; set; }

    public virtual DbSet<Hrprocedure> Hrprocedures { get; set; }

    public virtual DbSet<LeaveBalance> LeaveBalances { get; set; }

    public virtual DbSet<LeaveRequest> LeaveRequests { get; set; }

    public virtual DbSet<LeaveType> LeaveTypes { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<OvertimeRequest> OvertimeRequests { get; set; }

    public virtual DbSet<PayrollAllowance> PayrollAllowances { get; set; }

    public virtual DbSet<PayrollDeduction> PayrollDeductions { get; set; }

    public virtual DbSet<PayrollPeriod> PayrollPeriods { get; set; }

    public virtual DbSet<PayrollPolicy> PayrollPolicies { get; set; }

    public virtual DbSet<PayrollRecord> PayrollRecords { get; set; }

    public virtual DbSet<Payslip> Payslips { get; set; }

    public virtual DbSet<Menu> Menus { get; set; }

    public virtual DbSet<Permission> Permissions { get; set; }

    public virtual DbSet<Position> Positions { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<RolePermission> RolePermissions { get; set; }

    public virtual DbSet<RoleMenu> RoleMenus { get; set; }

    public virtual DbSet<Shift> Shifts { get; set; }

    public virtual DbSet<ShiftAssignment> ShiftAssignments { get; set; }

    public virtual DbSet<SystemSetting> SystemSettings { get; set; }

    public virtual DbSet<Task> Tasks { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    public virtual DbSet<AttendanceLog> AttendanceLogs { get; set; }

    public virtual DbSet<FaceProfile> FaceProfiles { get; set; }
    public virtual DbSet<FaceVerificationLog> FaceVerificationLogs { get; set; }

    public virtual DbSet<ResignationRequest> ResignationRequests { get; set; }

    public virtual DbSet<PayrollFeedback> PayrollFeedbacks { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AttendanceRecord>(entity =>
        {
            entity.HasKey(e => e.AttendanceId).HasName("PK__Attendan__8B69263C44FB91E1");

            entity.HasIndex(e => e.AttendanceDate, "IX_Attendance_Date");

            // Every query is filtered to the current company and usually a date range
            entity.HasIndex(e => new { e.CompanyId, e.AttendanceDate }, "IX_AttendanceRecords_Company_Date");

            // The unique index also serves employee + date lookups; a second, non-unique copy only slowed writes
            entity.HasIndex(e => new { e.EmployeeId, e.AttendanceDate }, "UQ_Attendance_EmployeeDate").IsUnique();

            entity.Property(e => e.AttendanceId).HasColumnName("AttendanceID");
            entity.Property(e => e.CheckInTime).HasColumnType("datetime");
            entity.Property(e => e.CheckOutTime).HasColumnType("datetime");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.EarlyLeaveMinutes).HasDefaultValue(0);
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.ExplanationLeaveTypeId).HasColumnName("ExplanationLeaveTypeID");
            entity.Property(e => e.ExplanationRequestedCheckInTime).HasColumnType("time");
            entity.Property(e => e.ExplanationRequestedCheckOutTime).HasColumnType("time");
            entity.Property(e => e.ExplanationType).HasMaxLength(30);
            entity.Property(e => e.LateMinutes).HasDefaultValue(0);
            entity.Property(e => e.Location).HasMaxLength(100);
            entity.Property(e => e.ModifiedDate).HasColumnType("datetime");
            entity.Property(e => e.OvertimeHours)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(6, 2)");
            entity.Property(e => e.Remarks).HasMaxLength(500);
            entity.Property(e => e.ShiftId).HasColumnName("ShiftID");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Present");
            entity.Property(e => e.WorkingHours).HasColumnType("decimal(6, 2)");

            entity.HasOne(d => d.Employee).WithMany(p => p.AttendanceRecords)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AttendanceRecords_Employees");

            entity.HasOne(d => d.ExplanationLeaveType).WithMany()
                .HasForeignKey(d => d.ExplanationLeaveTypeId)
                .HasConstraintName("FK_AttendanceRecords_ExplanationLeaveTypes");

            entity.HasOne(d => d.Shift).WithMany(p => p.AttendanceRecords)
                .HasForeignKey(d => d.ShiftId)
                .HasConstraintName("FK_AttendanceRecords_Shifts");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditId).HasName("PK__AuditLog__A17F23B8E2BA6814");

            entity.HasIndex(e => e.ActionDate, "IX_AuditLogs_ActionDate");

            entity.HasIndex(e => e.TableName, "IX_AuditLogs_TableName");

            entity.HasIndex(e => e.UserId, "IX_AuditLogs_UserID");

            entity.Property(e => e.AuditId).HasColumnName("AuditID");
            entity.Property(e => e.Action).HasMaxLength(20);
            entity.Property(e => e.ActionDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Ipaddress)
                .HasMaxLength(50)
                .HasColumnName("IPAddress");
            entity.Property(e => e.RecordId).HasColumnName("RecordID");
            entity.Property(e => e.TableName).HasMaxLength(100);
            entity.Property(e => e.UserId).HasColumnName("UserID");

            entity.HasOne(d => d.User).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_AuditLogs_Users");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(e => e.DepartmentId).HasName("PK__Departme__B2079BCDDFB6E623");

            entity.HasIndex(e => new { e.CompanyId, e.DepartmentCode }, "UQ__Departme__6EA8896D0227EAE3").IsUnique();

            entity.Property(e => e.DepartmentId).HasColumnName("DepartmentID");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.DepartmentCode).HasMaxLength(20);
            entity.Property(e => e.DepartmentName).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ManagerId).HasColumnName("ManagerID");
            entity.Property(e => e.ModifiedDate).HasColumnType("datetime");
            entity.Property(e => e.ParentDepartmentId).HasColumnName("ParentDepartmentID");

            entity.HasOne(d => d.ParentDepartment).WithMany(p => p.InverseParentDepartment)
                .HasForeignKey(d => d.ParentDepartmentId)
                .HasConstraintName("FK_Departments_Parent");

            entity
        .HasOne(d => d.Manager)
        .WithMany()  
        .HasForeignKey(d => d.ManagerId)
        .OnDelete(DeleteBehavior.Restrict);
        }
        );

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.EmployeeId).HasName("PK__Employee__7AD04FF15162EFFE");

            entity.HasIndex(e => e.DepartmentId, "IX_Employees_DepartmentID");

            entity.HasIndex(e => e.Email, "IX_Employees_Email");

            entity.HasIndex(e => e.ManagerId, "IX_Employees_ManagerID");

            entity.HasIndex(e => e.PositionId, "IX_Employees_PositionID");

            entity.HasIndex(e => e.EmploymentStatus, "IX_Employees_Status");

            entity.HasIndex(e => new { e.CompanyId, e.EmployeeCode }, "UQ__Employee__1F642548E0EDEFF5").IsUnique();

            entity.HasIndex(e => new { e.CompanyId, e.Email }, "UQ__Employee__A9D105349FE0388B").IsUnique();

            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.BaseSalary).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.InsuranceSalary).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.City).HasMaxLength(50);
            entity.Property(e => e.Country).HasMaxLength(50);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.DepartmentId).HasColumnName("DepartmentID");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.EmployeeCode).HasMaxLength(20);
            entity.Property(e => e.EmploymentStatus)
                .HasMaxLength(20)
                .HasDefaultValue("Active");
            entity.Property(e => e.EmploymentType)
                .HasMaxLength(20)
                .HasDefaultValue("Full-Time");
            entity.Property(e => e.FirstName).HasMaxLength(50);
            entity.Property(e => e.FullName)
                .HasMaxLength(101)
                .HasComputedColumnSql("(([FirstName]+' ')+[LastName])", true);
            entity.Property(e => e.Gender).HasMaxLength(10);
            entity.Property(e => e.LastName).HasMaxLength(50);
            entity.Property(e => e.ManagerId).HasColumnName("ManagerID");
            entity.Property(e => e.ModifiedDate).HasColumnType("datetime");
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.PositionId).HasColumnName("PositionID");

            entity.HasOne(d => d.Department).WithMany(p => p.Employees)
                .HasForeignKey(d => d.DepartmentId)
                .HasConstraintName("FK_Employees_Departments");

            entity.HasOne(d => d.Manager).WithMany(p => p.InverseManager)
                .HasForeignKey(d => d.ManagerId)
                .HasConstraintName("FK_Employees_Manager");

            entity.HasOne(d => d.Position).WithMany(p => p.Employees)
                .HasForeignKey(d => d.PositionId)
                .HasConstraintName("FK_Employees_Positions");
        });

        modelBuilder.Entity<EmployeeContract>(entity =>
        {
            entity.HasKey(e => e.ContractId).HasName("PK__Employee__C90D34098EDB2497");

            entity.HasIndex(e => new { e.CompanyId, e.ContractNumber }, "UQ__Employee__C51D43DADFFA6EF0").IsUnique();

            entity.Property(e => e.ContractId).HasColumnName("ContractID");
            entity.Property(e => e.ContractNumber).HasMaxLength(50);
            entity.Property(e => e.ContractType).HasMaxLength(50);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.SalaryAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Active");

            entity.HasOne(d => d.Employee).WithMany(p => p.EmployeeContracts)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_EmployeeContracts_Employees");
        });

        modelBuilder.Entity<EmployeeDocument>(entity =>
        {
            entity.HasKey(e => e.DocumentId).HasName("PK__Employee__1ABEEF6FC626AC31");

            entity.Property(e => e.DocumentId).HasColumnName("DocumentID");
            entity.Property(e => e.DocumentCategory).HasMaxLength(50);
            entity.Property(e => e.DocumentTitle).HasMaxLength(200);
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.FilePath).HasMaxLength(500);
            entity.Property(e => e.FileType).HasMaxLength(10);
            entity.Property(e => e.ModifiedDate).HasColumnType("datetime");
            entity.Property(e => e.UploadDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Employee).WithMany(p => p.EmployeeDocuments)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_EmployeeDocuments_Employees");
        });

        modelBuilder.Entity<Holiday>(entity =>
        {
            entity.HasKey(e => e.HolidayId);

            // One entry per company per day
            entity.HasIndex(e => new { e.CompanyId, e.HolidayDate }, "UQ_Holidays_Company_Date").IsUnique();

            entity.Property(e => e.HolidayId).HasColumnName("HolidayID");
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.IsPaid).HasDefaultValue(true);
            entity.Property(e => e.CreatedDate).HasColumnType("datetime");
        });

        modelBuilder.Entity<Hrprocedure>(entity =>
        {
            entity.HasKey(e => e.ProcedureId).HasName("PK__HRProced__54C2E50D0DCD81AE");

            entity.ToTable("HRProcedures");

            entity.HasIndex(e => new { e.CompanyId, e.ProcedureNumber }, "UQ__HRProced__AA41A753D36A3E49").IsUnique();

            entity.Property(e => e.ProcedureId).HasColumnName("ProcedureID");
            entity.Property(e => e.ApprovedDate).HasColumnType("datetime");
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.NewDepartmentId).HasColumnName("NewDepartmentID");
            entity.Property(e => e.NewPositionId).HasColumnName("NewPositionID");
            entity.Property(e => e.NewSalary).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.ProcedureNumber).HasMaxLength(50);
            entity.Property(e => e.ProcedureType).HasMaxLength(50);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.ReviewedDate).HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending");
            entity.Property(e => e.SubmittedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.Employee).WithMany(p => p.Hrprocedures)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_HRProcedures_Employees");

            entity.HasOne(d => d.NewDepartment).WithMany(p => p.Hrprocedures)
                .HasForeignKey(d => d.NewDepartmentId)
                .HasConstraintName("FK_HRProcedures_NewDepartment");

            entity.HasOne(d => d.NewPosition).WithMany(p => p.Hrprocedures)
                .HasForeignKey(d => d.NewPositionId)
                .HasConstraintName("FK_HRProcedures_NewPosition");

            entity.HasOne(d => d.NewManager).WithMany()
                .HasForeignKey(d => d.NewManagerId)
                .HasConstraintName("FK_HRProcedures_NewManager")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.AppliedByNavigation).WithMany()
                .HasForeignKey(d => d.AppliedBy)
                .HasConstraintName("FK_HRProcedures_AppliedBy")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LeaveBalance>(entity =>
        {
            entity.HasKey(e => e.BalanceId).HasName("PK__LeaveBal__A760D59E8B49FB3E");

            entity.HasIndex(e => new { e.EmployeeId, e.LeaveTypeId, e.Year }, "UQ_LeaveBalances").IsUnique();

            entity.Property(e => e.BalanceId).HasColumnName("BalanceID");
            entity.Property(e => e.CarriedForward).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.LastUpdated)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.LeaveTypeId).HasColumnName("LeaveTypeID");
            entity.Property(e => e.RemainingDays)
                .HasComputedColumnSql("([TotalEntitlement]-[UsedDays])", true)
                .HasColumnType("decimal(6, 2)");
            entity.Property(e => e.TotalEntitlement).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.UsedDays).HasColumnType("decimal(5, 2)");

            entity.HasOne(d => d.Employee).WithMany(p => p.LeaveBalances)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LeaveBalances_Employees");

            entity.HasOne(d => d.LeaveType).WithMany(p => p.LeaveBalances)
                .HasForeignKey(d => d.LeaveTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LeaveBalances_LeaveTypes");
        });

        modelBuilder.Entity<LeaveRequest>(entity =>
        {
            entity.HasKey(e => e.LeaveRequestId).HasName("PK__LeaveReq__6094218E199159EC");

            entity.HasIndex(e => new { e.StartDate, e.EndDate }, "IX_LeaveRequests_Dates");

            entity.HasIndex(e => e.EmployeeId, "IX_LeaveRequests_EmployeeID");

            entity.HasIndex(e => e.Status, "IX_LeaveRequests_Status");

            entity.HasIndex(e => new { e.CompanyId, e.RequestNumber }, "UQ__LeaveReq__9ADA6BE0F25CECE0").IsUnique();

            entity.Property(e => e.LeaveRequestId).HasColumnName("LeaveRequestID");
            entity.Property(e => e.ApprovedDate).HasColumnType("datetime");
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.LeaveTypeId).HasColumnName("LeaveTypeID");
            entity.Property(e => e.NumberOfDays).HasColumnType("decimal(3, 1)");
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.RequestNumber).HasMaxLength(50);
            entity.Property(e => e.ReviewedDate).HasColumnType("datetime");
            entity.Property(e => e.ReviewerComments).HasMaxLength(500);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending");
            entity.Property(e => e.SubmittedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.ApprovedByNavigation).WithMany(p => p.LeaveRequestApprovedByNavigations)
                .HasForeignKey(d => d.ApprovedBy)
                .HasConstraintName("FK_LeaveRequests_ApprovedBy");

            entity.HasOne(d => d.Employee).WithMany(p => p.LeaveRequests)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LeaveRequests_Employees");

            entity.HasOne(d => d.LeaveType).WithMany(p => p.LeaveRequests)
                .HasForeignKey(d => d.LeaveTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LeaveRequests_LeaveTypes");

            entity.HasOne(d => d.ReviewedByNavigation).WithMany(p => p.LeaveRequestReviewedByNavigations)
                .HasForeignKey(d => d.ReviewedBy)
                .HasConstraintName("FK_LeaveRequests_ReviewedBy");

            entity.HasOne(d => d.TargetApprover).WithMany()
                .HasForeignKey(d => d.TargetApproverId)
                .HasConstraintName("FK_LeaveRequests_TargetApprover");
        });

        modelBuilder.Entity<LeaveType>(entity =>
        {
            entity.HasKey(e => e.LeaveTypeId).HasName("PK__LeaveTyp__43BE8FF4F7AA7C4D");

            entity.HasIndex(e => new { e.CompanyId, e.LeaveTypeCode }, "UQ__LeaveTyp__A264FAEECF215F7C").IsUnique();

            entity.Property(e => e.LeaveTypeId).HasColumnName("LeaveTypeID");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.IsPaid).HasDefaultValue(true);
            entity.Property(e => e.LeaveTypeCode).HasMaxLength(20);
            entity.Property(e => e.LeaveTypeName).HasMaxLength(50);
            entity.Property(e => e.MaxCarryForwardDays).HasDefaultValue(0);
            entity.Property(e => e.RequiresApproval).HasDefaultValue(true);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("PK__Notifica__20CF2E3219D9C223");

            entity.HasIndex(e => e.IsRead, "IX_Notifications_IsRead");

            entity.HasIndex(e => e.RecipientUserId, "IX_Notifications_RecipientUserID");

            entity.Property(e => e.NotificationId).HasColumnName("NotificationID");
            entity.Property(e => e.NotificationType).HasMaxLength(50);
            entity.Property(e => e.ReadDate).HasColumnType("datetime");
            entity.Property(e => e.RecipientUserId).HasColumnName("RecipientUserID");
            entity.Property(e => e.RelatedEntity).HasMaxLength(50);
            entity.Property(e => e.RelatedEntityId).HasColumnName("RelatedEntityID");
            entity.Property(e => e.SentDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasOne(d => d.RecipientUser).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.RecipientUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Notifications_Users");
        });

        modelBuilder.Entity<OvertimeRequest>(entity =>
        {
            entity.HasKey(e => e.OvertimeRequestId).HasName("PK__Overtime__F97D0DAA14CF4EFC");

            entity.HasIndex(e => e.EmployeeId, "IX_OvertimeRequests_EmployeeID");

            entity.HasIndex(e => e.Status, "IX_OvertimeRequests_Status");

            entity.HasIndex(e => new { e.CompanyId, e.RequestNumber }, "UQ__Overtime__9ADA6BE0AE2DCA21").IsUnique();

            entity.Property(e => e.OvertimeRequestId).HasColumnName("OvertimeRequestID");
            entity.Property(e => e.ApprovedDate).HasColumnType("datetime");
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.RequestNumber).HasMaxLength(50);
            entity.Property(e => e.ReviewedDate).HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending");
            entity.Property(e => e.SubmittedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.TotalHours).HasColumnType("decimal(4, 2)");

            entity.HasOne(d => d.ApprovedByNavigation).WithMany(p => p.OvertimeRequestApprovedByNavigations)
                .HasForeignKey(d => d.ApprovedBy)
                .HasConstraintName("FK_OvertimeRequests_ApprovedBy");

            entity.HasOne(d => d.Employee).WithMany(p => p.OvertimeRequests)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OvertimeRequests_Employees");

            entity.HasOne(d => d.ReviewedByNavigation).WithMany(p => p.OvertimeRequestReviewedByNavigations)
                .HasForeignKey(d => d.ReviewedBy)
                .HasConstraintName("FK_OvertimeRequests_ReviewedBy");

            entity.HasOne(d => d.TargetApprover).WithMany()
                .HasForeignKey(d => d.TargetApproverId)
                .HasConstraintName("FK_OvertimeRequests_TargetApprover");
        });

        modelBuilder.Entity<PayrollAllowance>(entity =>
        {
            entity.HasKey(e => e.AllowanceId).HasName("PK__PayrollA__7B12D0418866292B");

            entity.Property(e => e.AllowanceId).HasColumnName("AllowanceID");
            entity.Property(e => e.AllowanceName).HasMaxLength(100);
            entity.Property(e => e.AllowanceType).HasMaxLength(50);
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.PayrollRecordId).HasColumnName("PayrollRecordID");

            entity.HasOne(d => d.PayrollRecord).WithMany(p => p.PayrollAllowances)
                .HasForeignKey(d => d.PayrollRecordId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PayrollAllowances_Records");
        });

        modelBuilder.Entity<PayrollDeduction>(entity =>
        {
            entity.HasKey(e => e.DeductionId).HasName("PK__PayrollD__E2604C770050657D");

            entity.Property(e => e.DeductionId).HasColumnName("DeductionID");
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.DeductionName).HasMaxLength(100);
            entity.Property(e => e.DeductionType).HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.PayrollRecordId).HasColumnName("PayrollRecordID");

            entity.HasOne(d => d.PayrollRecord).WithMany(p => p.PayrollDeductions)
                .HasForeignKey(d => d.PayrollRecordId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PayrollDeductions_Records");
        });

        modelBuilder.Entity<PayrollPeriod>(entity =>
        {
            entity.HasKey(e => e.PeriodId).HasName("PK__PayrollP__E521BB362CCDBB5F");

            entity.HasIndex(e => new { e.CompanyId, e.Month, e.Year }, "UQ_PayrollPeriods").IsUnique();

            entity.Property(e => e.PeriodId).HasColumnName("PeriodID");
            entity.Property(e => e.ApprovedDate).HasColumnType("datetime");
            entity.Property(e => e.CalculatedDate).HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Open");
        });

        modelBuilder.Entity<PayrollPolicy>(entity =>
        {
            entity.HasKey(e => e.PolicyId).HasName("PK__PayrollP__2E133944C0C8A2B6");

            entity.HasIndex(e => new { e.CompanyId, e.PolicyName }, "UQ__PayrollP__251851158851FA7E").IsUnique();

            entity.Property(e => e.PolicyId).HasColumnName("PolicyID");
            entity.Property(e => e.ApplicableEmployeeGroup).HasMaxLength(100);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ModifiedDate).HasColumnType("datetime");
            entity.Property(e => e.PolicyName).HasMaxLength(100);
            entity.Property(e => e.PolicyType).HasMaxLength(50);
        });

        modelBuilder.Entity<PayrollRecord>(entity =>
        {
            entity.HasKey(e => e.PayrollRecordId).HasName("PK__PayrollR__17BE4B705331E66F");

            entity.HasIndex(e => e.EmployeeId, "IX_PayrollRecords_EmployeeID");

            entity.HasIndex(e => e.PeriodId, "IX_PayrollRecords_PeriodID");

            entity.HasIndex(e => new { e.EmployeeId, e.PeriodId }, "UQ_PayrollRecords").IsUnique();

            entity.Property(e => e.PayrollRecordId).HasColumnName("PayrollRecordID");
            entity.Property(e => e.ActualWorkingDays).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.ApprovedDate).HasColumnType("datetime");
            entity.Property(e => e.BaseSalary).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.BonusAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CalculatedDate).HasColumnType("datetime");
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.GrossPay)
                .HasComputedColumnSql("((([BaseSalary]+[TotalAllowances])+[OvertimePay])+[BonusAmount])", true)
                .HasColumnType("decimal(21, 2)");
            entity.Property(e => e.InsuranceAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.NetPay)
                .HasComputedColumnSql("(((((([BaseSalary]+[TotalAllowances])+[OvertimePay])+[BonusAmount])-[TotalDeductions])-[TaxAmount])-[InsuranceAmount])", true)
                .HasColumnType("decimal(24, 2)");
            entity.Property(e => e.OvertimePay).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PeriodId).HasColumnName("PeriodID");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Draft");
            entity.Property(e => e.TaxAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TotalAllowances).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TotalDeductions).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.WorkingDays).HasColumnType("decimal(5, 2)");

            entity.HasOne(d => d.Employee).WithMany(p => p.PayrollRecords)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PayrollRecords_Employees");

            entity.HasOne(d => d.Period).WithMany(p => p.PayrollRecords)
                .HasForeignKey(d => d.PeriodId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PayrollRecords_Periods");
        });

        modelBuilder.Entity<Payslip>(entity =>
        {
            entity.HasKey(e => e.PayslipId).HasName("PK__Payslips__6EDC7142D154B313");

            entity.HasIndex(e => new { e.CompanyId, e.PayslipNumber }, "UQ__Payslips__38A71BAD66586385").IsUnique();

            entity.Property(e => e.PayslipId).HasColumnName("PayslipID");
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.GeneratedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.PayrollRecordId).HasColumnName("PayrollRecordID");
            entity.Property(e => e.PayslipNumber).HasMaxLength(50);
            entity.Property(e => e.Pdfpath)
                .HasMaxLength(500)
                .HasColumnName("PDFPath");
            entity.Property(e => e.PeriodId).HasColumnName("PeriodID");
            entity.Property(e => e.ViewedDate).HasColumnType("datetime");

            entity.HasOne(d => d.Employee).WithMany(p => p.Payslips)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payslips_Employees");

            entity.HasOne(d => d.PayrollRecord).WithMany(p => p.Payslips)
                .HasForeignKey(d => d.PayrollRecordId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payslips_Records");

            entity.HasOne(d => d.Period).WithMany(p => p.Payslips)
                .HasForeignKey(d => d.PeriodId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payslips_Periods");
        });

        modelBuilder.Entity<Position>(entity =>
        {
            entity.HasKey(e => e.PositionId).HasName("PK__Position__60BB9A59726208B5");

            entity.HasIndex(e => new { e.CompanyId, e.PositionCode }, "UQ__Position__83745B02ABEA23B6").IsUnique();

            entity.Property(e => e.PositionId).HasColumnName("PositionID");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Level).HasDefaultValue(1);
            entity.Property(e => e.IsTopLevel).HasDefaultValue(false);
            entity.Property(e => e.PositionCode).HasMaxLength(20);
            entity.Property(e => e.PositionName).HasMaxLength(100);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PK__Roles__8AFACE3A81AE763D");

            entity.HasIndex(e => new { e.CompanyId, e.RoleName }, "UQ__Roles__8A2B6160EED0EB46").IsUnique();

            entity.Property(e => e.RoleId).HasColumnName("RoleID");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ModifiedDate).HasColumnType("datetime");
            entity.Property(e => e.RoleName).HasMaxLength(50);
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(e => e.PermissionId);

            entity.HasIndex(e => e.PermissionKey).IsUnique();

            entity.Property(e => e.PermissionId).HasColumnName("PermissionID");
            entity.Property(e => e.PermissionKey).HasMaxLength(100);
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Menu>(entity =>
        {
            entity.HasKey(e => e.MenuId);

            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => new { e.ParentId, e.DisplayOrder });

            entity.Property(e => e.MenuId).HasColumnName("MenuID");
            entity.Property(e => e.ParentId).HasColumnName("ParentID");
            entity.Property(e => e.Code).HasMaxLength(100);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Route).HasMaxLength(300);
            entity.Property(e => e.IconName).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Parent).WithMany(p => p.Children)
                .HasForeignKey(d => d.ParentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Menus_Parent");
        });

        modelBuilder.Entity<RoleMenu>(entity =>
        {
            entity.HasKey(e => new { e.RoleId, e.MenuId });

            entity.Property(e => e.RoleId).HasColumnName("RoleID");
            entity.Property(e => e.MenuId).HasColumnName("MenuID");

            entity.HasOne(d => d.Role).WithMany(p => p.RoleMenus)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_RoleMenus_Roles");

            entity.HasOne(d => d.Menu).WithMany(p => p.RoleMenus)
                .HasForeignKey(d => d.MenuId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_RoleMenus_Menus");
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(e => e.RolePermissionId);

            entity.HasIndex(e => new { e.RoleId, e.PermissionId }, "UQ_RolePermissions").IsUnique();

            entity.Property(e => e.RolePermissionId).HasColumnName("RolePermissionID");
            entity.Property(e => e.RoleId).HasColumnName("RoleID");
            entity.Property(e => e.PermissionId).HasColumnName("PermissionID");

            entity.HasOne(d => d.Role).WithMany(p => p.RolePermissions)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_RolePermissions_Roles");

            entity.HasOne(d => d.Permission).WithMany(p => p.RolePermissions)
                .HasForeignKey(d => d.PermissionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_RolePermissions_Permissions");
        });

        modelBuilder.Entity<Shift>(entity =>
        {
            entity.HasKey(e => e.ShiftId).HasName("PK__Shifts__C0A838E11F3BCF8E");

            entity.HasIndex(e => new { e.CompanyId, e.ShiftCode }, "UQ__Shifts__9377D5623409151A").IsUnique();

            entity.Property(e => e.ShiftId).HasColumnName("ShiftID");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.ShiftCode).HasMaxLength(20);
            entity.Property(e => e.ShiftName).HasMaxLength(50);
            entity.Property(e => e.ShiftType)
                .HasMaxLength(20)
                .HasDefaultValue("Regular");
        });

        modelBuilder.Entity<ShiftAssignment>(entity =>
        {
            entity.HasKey(e => e.AssignmentId).HasName("PK__ShiftAss__32499E573BC5AE0C");

            // One row per employee per day: queries look up a company's day range or one employee's day
            entity.HasIndex(e => new { e.CompanyId, e.AssignmentDate }, "IX_ShiftAssignments_Company_Date");
            entity.HasIndex(e => new { e.EmployeeId, e.AssignmentDate }, "IX_ShiftAssignments_Employee_Date");

            entity.Property(e => e.AssignmentId).HasColumnName("AssignmentID");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.RecurrencePattern).HasMaxLength(50);
            entity.Property(e => e.ShiftId).HasColumnName("ShiftID");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Active");

            entity.HasOne(d => d.Employee).WithMany(p => p.ShiftAssignments)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ShiftAssignments_Employees");

            entity.HasOne(d => d.Shift).WithMany(p => p.ShiftAssignments)
                .HasForeignKey(d => d.ShiftId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ShiftAssignments_Shifts");
        });

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.SettingId).HasName("PK__SystemSe__54372AFD0CC988E3");

            entity.HasIndex(e => new { e.CompanyId, e.SettingKey }, "UQ__SystemSe__01E719AD19FD850C").IsUnique();

            entity.Property(e => e.SettingId).HasColumnName("SettingID");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ModifiedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.SettingCategory).HasMaxLength(50);
            entity.Property(e => e.SettingKey).HasMaxLength(100);
        });

        modelBuilder.Entity<Task>(entity =>
        {
            entity.HasKey(e => e.TaskId).HasName("PK__Tasks__7C6949D1D79FA7B9");

            entity.HasIndex(e => e.AssignedTo, "IX_Tasks_AssignedTo");

            entity.HasIndex(e => e.DueDate, "IX_Tasks_DueDate");

            entity.HasIndex(e => e.Status, "IX_Tasks_Status");

            entity.Property(e => e.TaskId).HasColumnName("TaskID");
            entity.Property(e => e.CompletedDate).HasColumnType("datetime");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Priority)
                .HasMaxLength(20)
                .HasDefaultValue("Medium");
            entity.Property(e => e.RelatedRequestId).HasColumnName("RelatedRequestID");
            entity.Property(e => e.RelatedRequestType).HasMaxLength(50);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending");
            entity.Property(e => e.TaskTitle).HasMaxLength(200);
            entity.Property(e => e.TaskType).HasMaxLength(50);

            entity.HasOne(d => d.AssignedToNavigation).WithMany(p => p.Tasks)
                .HasForeignKey(d => d.AssignedTo)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Tasks_AssignedTo");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CCACE4BDE2F5");

            entity.HasIndex(e => e.Email, "IX_Users_Email");

            entity.HasIndex(e => e.EmployeeId, "IX_Users_EmployeeID");

            entity.HasIndex(e => e.IsActive, "IX_Users_IsActive");

            entity.HasIndex(e => e.Username, "UQ__Users__536C85E4189298C2").IsUnique();

            entity.HasIndex(e => e.Email, "UQ__Users__A9D105343C39E404").IsUnique();

            entity.Property(e => e.UserId).HasColumnName("UserID");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastLogin).HasColumnType("datetime");
            entity.Property(e => e.ModifiedDate).HasColumnType("datetime");
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
            entity.Property(e => e.Username).HasMaxLength(50);

            entity.HasOne(d => d.Employee).WithMany(p => p.Users)
                .HasForeignKey(d => d.EmployeeId)
                .HasConstraintName("FK_Users_Employees");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => e.UserRoleId).HasName("PK__UserRole__3D978A55E65D6156");

            entity.HasIndex(e => new { e.UserId, e.RoleId }, "UQ_UserRoles").IsUnique();

            entity.Property(e => e.UserRoleId).HasColumnName("UserRoleID");
            entity.Property(e => e.AssignedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.RoleId).HasColumnName("RoleID");
            entity.Property(e => e.UserId).HasColumnName("UserID");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserRoles_Roles");

            entity.HasOne(d => d.User).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserRoles_Users");
        });

        modelBuilder.Entity<AttendanceLog>(entity =>
        {
            entity.HasKey(e => e.LogId).HasName("PK__Attendan__5E5499A8");

            entity.Property(e => e.LogId).HasColumnName("LogID");
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.ShiftId).HasColumnName("ShiftID");
            entity.Property(e => e.LogTime).HasColumnType("datetime");
            entity.Property(e => e.LogType).HasMaxLength(20);
            entity.Property(e => e.Source)
                .HasMaxLength(20)
                .HasDefaultValue("Web");
            entity.Property(e => e.DeviceInfo).HasMaxLength(255);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.Location).HasMaxLength(100);
            entity.Property(e => e.Remarks).HasMaxLength(500);
            entity.Property(e => e.IsValid).HasDefaultValue(true);
            entity.Property(e => e.CreatedDate)
                .HasColumnType("datetime")
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.CreatedBy).HasColumnName("CreatedBy");

            entity.HasOne(d => d.Employee).WithMany()
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AttendanceLogs_Employees");

            entity.HasOne(d => d.Shift).WithMany()
                .HasForeignKey(d => d.ShiftId)
                .HasConstraintName("FK_AttendanceLogs_Shifts");
        });

        modelBuilder.Entity<ResignationRequest>(entity =>
        {
            entity.HasKey(e => e.ResignationRequestId);

            entity.HasIndex(e => new { e.CompanyId, e.RequestNumber }).IsUnique();

            entity.Property(e => e.RequestNumber).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Pending");
            entity.Property(e => e.Reason).HasMaxLength(1000);
            entity.Property(e => e.HandoverNote).HasMaxLength(2000);
            entity.Property(e => e.RejectionReason).HasMaxLength(1000);
            entity.Property(e => e.ReviewerComments).HasMaxLength(1000);
            entity.Property(e => e.SubmittedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ReviewedDate).HasColumnType("datetime");

            entity.HasOne(d => d.Employee)
                .WithMany()
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ResignationRequests_Employees");

            entity.HasOne(d => d.HandoverToEmployee)
                .WithMany()
                .HasForeignKey(d => d.HandoverToEmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ResignationRequests_HandoverEmployee");

            entity.HasOne(d => d.ReviewedByNavigation)
                .WithMany()
                .HasForeignKey(d => d.ReviewedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ResignationRequests_ReviewedBy");

            entity.HasOne(d => d.TargetApprover)
                .WithMany()
                .HasForeignKey(d => d.TargetApproverId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ResignationRequests_TargetApprover");
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(e => e.CompanyId);

            entity.HasIndex(e => e.CompanyCode).IsUnique();

            entity.Property(e => e.CompanyId).HasColumnName("CompanyID");
            entity.Property(e => e.CompanyCode).HasMaxLength(20);
            entity.Property(e => e.CompanyName).HasMaxLength(200);
            entity.Property(e => e.TaxCode).HasMaxLength(20);
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ModifiedDate).HasColumnType("datetime");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.CompanyId).HasColumnName("CompanyID");

            entity.HasOne(d => d.Company).WithMany()
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Users_Companies");

            entity.HasQueryFilter(u => CurrentCompanyId != null && u.CompanyId == CurrentCompanyId);
        });

        // Junction tables have no CompanyId of their own: filter them through their owning role / user
        modelBuilder.Entity<RolePermission>()
            .HasQueryFilter(rp => CurrentCompanyId != null && rp.Role.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<RoleMenu>()
            .HasQueryFilter(rm => CurrentCompanyId != null && rm.Role.CompanyId == CurrentCompanyId);
        modelBuilder.Entity<UserRole>()
            .HasQueryFilter(ur => CurrentCompanyId != null && ur.User.CompanyId == CurrentCompanyId);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(ICompanyScoped).IsAssignableFrom(t.ClrType))
                     .ToList())
        {
            ConfigureCompanyScopedMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, new object[] { modelBuilder });
        }

        OnModelCreatingPartial(modelBuilder);
    }

    private static readonly System.Reflection.MethodInfo ConfigureCompanyScopedMethod =
        typeof(HrmsDbContext).GetMethod(nameof(ConfigureCompanyScoped),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    private void ConfigureCompanyScoped<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ICompanyScoped
    {
        var entity = modelBuilder.Entity<TEntity>();

        entity.Property(e => e.CompanyId).HasColumnName("CompanyID");

        entity.HasOne<Company>().WithMany()
            .HasForeignKey(e => e.CompanyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName($"FK_{entity.Metadata.GetTableName()}_Companies");

        entity.HasQueryFilter(e => CurrentCompanyId != null && e.CompanyId == CurrentCompanyId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AssignCompanyToNewEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AssignCompanyToNewEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Bản ghi mới tự nhận công ty hiện tại, nên service không phải gán CompanyId.
    /// Không có công ty thì từ chối lưu để không tạo ra dữ liệu "mồ côi".
    /// </summary>
    private void AssignCompanyToNewEntities()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
                continue;

            if (entry.Entity is ICompanyScoped scoped && scoped.CompanyId == 0)
            {
                scoped.CompanyId = CurrentCompanyId
                    ?? throw new InvalidOperationException(
                        $"Không xác định được công ty khi tạo {entry.Metadata.ClrType.Name}.");
            }
            else if (entry.Entity is User user && user.CompanyId == null && CurrentCompanyId != null)
            {
                user.CompanyId = CurrentCompanyId;
            }
        }
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
