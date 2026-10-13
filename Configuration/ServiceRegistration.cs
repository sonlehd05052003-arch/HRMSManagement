using HRManagement.Authorization;
using HRManagement.DataAcess;
using HRManagement.DataAcess.Implementations;
using HRManagement.DataAcess.Interfaces;
using HRManagement.Services.Attendances;
using HRManagement.Services.Cloudinaries;
using HRManagement.Services.Companies;
using HRManagement.Services.CurrentUsers;
using HRManagement.Services.Departments;
using HRManagement.Services.Emails;
using HRManagement.Services.Employees;
using HRManagement.Services.FaceVerifications;
using HRManagement.Services.FileStorages;
using HRManagement.Services.HRProceduces;
using HRManagement.Services.Holidays;
using HRManagement.Services.Leaves;
using HRManagement.Services.Notifications;
using HRManagement.Services.Overtimes;
using HRManagement.Services.Positions;
using HRManagement.Services.Shifts;
using HRManagement.Services.Users;
using HRManagement.Services.Tasks;
using HRManagement.Services.Approvals;
using HRManagement.Services.Audits;
using HRManagement.Services.Exports;
using HRManagement.Services.Backgrounds;
using HRManagement.Services.Payroll;
using HRManagement.Services.Resignations;
using HRManagement.Services.Tenants;

namespace HRManagement.Configuration
{
    /// <summary>The project's own repositories and services, registered from Program.cs.</summary>
    public static class ServiceRegistration
    {
        public static IServiceCollection AddRepositories(this IServiceCollection services)
        {
            services.AddScoped<IEmployeeRepository, EmployeeRepository>();
            services.AddScoped<IEmployeeDocumentRepository, EmployeeDocumentRepository>();
            services.AddScoped<IHRProcedureRepository, HRProcedureRepository>();
            services.AddScoped<IDepartmentRepository, DepartmentRepository>();
            services.AddScoped<IPositionRepository, PositionRepository>();
            services.AddScoped<IAttendanceRepository, AttendanceRepository>();
            services.AddScoped<IShiftRepository, ShiftRepository>();
            services.AddScoped<IShiftAssignmentRepository, ShiftAssignmentRepository>();

            // User, Auth & Task Repositories
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IAuthRepository, AuthRepository>();
            services.AddScoped<ITaskRepository, TaskRepository>();

            // Payroll Repositories
            services.AddScoped<IPayrollRepository, PayrollRepository>();
            services.AddScoped<IPayrollPeriodRepository, PayrollPeriodRepository>();

            return services;
        }

        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddScoped<ITenantContext, TenantContext>();
            services.AddScoped<ICompanyService, CompanyService>();
            services.AddScoped<ICurrentUserService, CurrentUserService>();
            services.AddScoped<IEmployeeService, EmployeeService>();
            services.AddScoped<IEmployeeImportService, EmployeeImportService>();
            services.AddScoped<ICloudinaryService, CloudinaryService>();
            services.AddScoped<IEmployeeDocumentService, EmployeeDocumentService>();
            services.AddScoped<IEmployeeContractService, EmployeeContractService>();
            // Email: services enqueue through QueuedEmailService; Hangfire runs EmailService (SMTP) in the background
            services.AddScoped<EmailService>();
            services.AddScoped<IEmailService, QueuedEmailService>();
            services.AddScoped<IHRProcedureService, HRProcedureService>();
            services.AddScoped<IDepartmentService, DepartmentService>();
            services.AddScoped<IPositionService, PositionService>();
            services.AddScoped<ILeaveBalanceService, LeaveBalanceService>();
            services.AddScoped<ILeaveTypeService, LeaveTypeService>();
            services.AddScoped<ILeaveRequestService, LeaveRequestService>();
            services.AddScoped<IAttendanceService, AttendanceService>();
            services.AddScoped<IFaceVerificationService, FaceVerificationService>();
            services.AddScoped<IFileStorageService, FileStorageService>();
            services.AddScoped<IShiftService, ShiftService>();
            services.AddScoped<IShiftAssignmentService, ShiftAssignmentService>();
            services.AddScoped<IOvertimeRequestService, OvertimeRequestService>();
            services.AddScoped<IUserAccountValidationService, UserAccountValidationService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<ITaskService, TaskService>();
            services.AddScoped<ITopLevelResolver, TopLevelResolver>();
            services.AddScoped<IApprovalRouteService, ApprovalRouteService>();
            services.AddScoped<FaceEmbeddingService>();

            // Specialized Services
            services.AddScoped<IAuditService, AuditService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IHolidayService, HolidayService>();
            services.AddScoped<IExportService, ExportService>();

            // Resignation Request
            services.AddScoped<IResignationRequestService, ResignationRequestService>();

            // Payroll Services
            services.AddScoped<TaxCalculationService>();
            services.AddScoped<IPayrollService, PayrollService>();

            return services;
        }
    }
}
