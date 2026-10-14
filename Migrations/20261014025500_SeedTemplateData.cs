using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRManagement.Migrations
{
    /// <summary>
    /// Data a new database needs: the permission and menu catalogue, and company 1, the template that
    /// CompanyService copies roles (with their permissions and menus), leave types, shifts, allowance
    /// policies and payroll settings from when a company is created. The platform admin is created at
    /// start-up from configuration (SuperAdminSeeder).
    /// </summary>
    public partial class SeedTemplateData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET IDENTITY_INSERT [Companies] ON;
INSERT INTO [Companies] ([CompanyID], [CompanyCode], [CompanyName], [IsActive], [CreatedDate]) VALUES (1, N'DEFAULT', N'Công ty mặc định', 1, GETDATE());
SET IDENTITY_INSERT [Companies] OFF;

-- Permissions
SET IDENTITY_INSERT [Permissions] ON;
INSERT INTO [Permissions] ([PermissionID], [PermissionKey], [DisplayName], [Category], [Description], [DisplayOrder], [IsActive]) VALUES
    (1, N'Dashboard.Admin', N'Xem tổng quan Admin', N'Tổng quan', NULL, 1, 1),
    (2, N'Dashboard.Hr', N'Xem tổng quan Nhân sự', N'Tổng quan', NULL, 1, 1),
    (3, N'Dashboard.Manager', N'Xem tổng quan Quản lý', N'Tổng quan', NULL, 1, 1),
    (4, N'User.View', N'Xem danh sách người dùng', N'Người dùng & Vai trò', NULL, 2, 1),
    (5, N'User.Manage', N'Thêm / sửa / khóa người dùng', N'Người dùng & Vai trò', NULL, 2, 1),
    (6, N'Role.Manage', N'Quản lý vai trò', N'Người dùng & Vai trò', NULL, 2, 0),
    (7, N'Menu.Manage', N'Quản lý menu & phân menu theo vai trò', N'Người dùng & Vai trò', NULL, 2, 0),
    (8, N'SystemSettings.ManageGeneral', N'Cấu hình vị trí chấm công & người duyệt dự phòng', N'Cấu hình hệ thống', NULL, 3, 1),
    (9, N'SystemSettings.ManagePayroll', N'Cấu hình kỳ lương', N'Cấu hình hệ thống', NULL, 3, 1),
    (10, N'SystemSettings.ManageAdvanced', N'Cấu hình tính lương & thông tin công ty', N'Cấu hình hệ thống', NULL, 3, 1),
    (11, N'Employee.Manage', N'Thêm / sửa nhân viên, phân tích tuyến duyệt', N'Nhân viên', NULL, 4, 1),
    (12, N'Attendance.Manage', N'Khóa / mở khóa bản ghi chấm công', N'Chấm công', NULL, 5, 1),
    (13, N'HRProcedure.Submit', N'Tạo thủ tục nhân sự', N'Thủ tục nhân sự', NULL, 6, 1),
    (14, N'HRProcedure.Manage', N'Sửa / xóa thủ tục nhân sự', N'Thủ tục nhân sự', NULL, 6, 1),
    (15, N'HRProcedure.Approve', N'Duyệt / từ chối / áp dụng thủ tục', N'Thủ tục nhân sự', NULL, 6, 1),
    (16, N'Payroll.View', N'Xem kỳ lương & bảng lương', N'Lương', NULL, 7, 1),
    (17, N'Payroll.Process', N'Tạo kỳ lương, tính lương, phụ cấp, khấu trừ, phiếu lương', N'Lương', NULL, 7, 1),
    (18, N'Payroll.Approve', N'Duyệt / từ chối kỳ lương, khóa công', N'Lương', NULL, 7, 1),
    (19, N'Reports.Export', N'Xuất báo cáo', N'Báo cáo', NULL, 8, 1),
    (20, N'AuditLog.View', N'Xem nhật ký thay đổi dữ liệu', N'Cấu hình hệ thống', NULL, 3, 1);
SET IDENTITY_INSERT [Permissions] OFF;

-- Menus
SET IDENTITY_INSERT [Menus] ON;
INSERT INTO [Menus] ([MenuID], [ParentID], [Code], [Title], [Route], [IconName], [DisplayOrder], [IsActive], [ShowForSuperAdmin]) VALUES
    (1, NULL, N'dashboard.admin', N'Tổng quan', N'/dashboard/admin', N'AppstoreOutlined', 1, 1, 0),
    (2, NULL, N'dashboard.hr', N'Tổng quan', N'/dashboard/hr', N'AppstoreOutlined', 2, 1, 0),
    (3, NULL, N'dashboard.manage', N'Tổng quan', N'/dashboard/manage', N'AppstoreOutlined', 3, 1, 0),
    (4, NULL, N'self.attendance', N'Chấm công của tôi', N'/attendance/my', N'ClockCircleOutlined', 4, 1, 0),
    (5, NULL, N'self.leave', N'Nghỉ phép của tôi', N'/leave/my', N'CalendarOutlined', 5, 1, 0),
    (6, NULL, N'self.overtime', N'Tăng ca của tôi', N'/overtime/my', N'ClockCircleOutlined', 6, 1, 0),
    (7, NULL, N'self.resignation', N'Đơn thôi việc', N'/resignation/my', N'IdcardOutlined', 7, 1, 0),
    (8, NULL, N'system', N'Hệ thống', NULL, N'SettingOutlined', 10, 1, 1),
    (9, NULL, N'org', N'Tổ chức', NULL, N'BankOutlined', 20, 1, 0),
    (10, NULL, N'time', N'Chấm công & Ca làm', NULL, N'ClockCircleOutlined', 30, 1, 0),
    (11, NULL, N'leave', N'Nghỉ phép & Tăng ca', NULL, N'CalendarOutlined', 40, 1, 0),
    (12, NULL, N'hrservices', N'Dịch vụ Nhân sự', NULL, N'IdcardOutlined', 50, 1, 0),
    (13, NULL, N'payroll', N'Lương & Thưởng', NULL, N'WalletOutlined', 60, 1, 0),
    (15, NULL, N'tasks', N'Quản lý công việc', N'/manage-task', N'TeamOutlined', 80, 1, 0),
    (16, NULL, N'config', N'Cấu hình', NULL, N'SettingOutlined', 90, 1, 0),
    (17, NULL, N'system.users', N'Quản lý người dùng', N'/admin/manage-user', NULL, 1, 1, 1),
    (18, NULL, N'system.roles', N'Quản lý vai trò', N'/admin/manage-role', NULL, 2, 1, 1),
    (19, NULL, N'system.permissions', N'Phân quyền', N'/admin/manage-permission', NULL, 3, 1, 1),
    (20, NULL, N'system.menus', N'Quản lý menu', N'/admin/manage-menu', NULL, 4, 1, 1),
    (21, NULL, N'system.settings', N'Cấu hình hệ thống', N'/admin/system-settings', NULL, 5, 1, 0),
    (22, NULL, N'org.departments', N'Quản lý phòng ban', N'/hr/manage-department', NULL, 1, 1, 0),
    (23, NULL, N'org.positions', N'Quản lý chức vụ', N'/hr/manage-position', NULL, 2, 1, 0),
    (24, NULL, N'org.employees', N'Quản lý nhân viên', N'/hr/manage-employee', NULL, 3, 1, 0),
    (25, NULL, N'time.my', N'Chấm công của tôi', N'/attendance/my', NULL, 1, 1, 0),
    (26, NULL, N'time.manage', N'Quản lý chấm công', N'/attendance/manage', NULL, 2, 1, 0),
    (27, NULL, N'time.face', N'Đăng ký khuôn mặt', N'/hr/manage-face-registration', NULL, 3, 1, 0),
    (28, NULL, N'time.shifts', N'Quản lý ca', N'/hr/manage-shift', NULL, 4, 1, 0),
    (29, NULL, N'time.assignments', N'Phân ca làm việc', N'/hr/manage-shift-assignment', NULL, 5, 1, 0),
    (30, NULL, N'leave.my', N'Nghỉ phép của tôi', N'/leave/my', NULL, 1, 1, 0),
    (31, NULL, N'leave.manage', N'Duyệt nghỉ phép', N'/leave/manage', NULL, 2, 1, 0),
    (32, NULL, N'leave.config', N'Cấu hình nghỉ phép', N'/leave/configuration', NULL, 3, 1, 0),
    (33, NULL, N'overtime.my', N'Tăng ca của tôi', N'/overtime/my', NULL, 4, 1, 0),
    (34, NULL, N'overtime.manage', N'Duyệt tăng ca', N'/overtime/manage', NULL, 5, 1, 0),
    (35, NULL, N'resignation.my', N'Đơn thôi việc của tôi', N'/resignation/my', NULL, 6, 1, 0),
    (36, NULL, N'resignation.manage', N'Duyệt đơn thôi việc', N'/resignation/manage', NULL, 7, 1, 0),
    (37, NULL, N'hrservices.procedures', N'Quản lý thủ tục', N'/hr/manage-procedure', NULL, 1, 1, 0),
    (38, NULL, N'hrservices.resignation', N'Đơn thôi việc của tôi', N'/resignation/my', NULL, 2, 1, 0),
    (39, NULL, N'payroll.periods', N'Quản lý kỳ lương', N'/payroll/periods', NULL, 1, 1, 0),
    (40, NULL, N'payroll.mydraft', N'Xem chấm công', N'/payroll/my-draft', NULL, 2, 1, 0),
    (41, NULL, N'payroll.myslips', N'Phiếu lương của tôi', N'/payroll/my-payslips', NULL, 3, 1, 0),
    (42, NULL, N'payroll.report', N'Báo cáo quỹ lương', N'/payroll/report', NULL, 4, 1, 0),
    (43, NULL, N'payroll.methodology', N'Quy tắc tính lương', N'/payroll/methodology', NULL, 5, 1, 0),
    (45, NULL, N'config.payroll', N'Cấu hình kỳ lương', N'/hr/payroll-settings', NULL, 1, 1, 0),
    (49, NULL, N'platform.companies', N'Quản lý công ty', N'/superadmin/companies', N'BankOutlined', 0, 1, 1),
    (50, NULL, N'system.auditlog', N'Nhật ký thay đổi', N'/admin/audit-log', NULL, 6, 1, 0);
SET IDENTITY_INSERT [Menus] OFF;

UPDATE m SET m.[ParentID] = p.ParentID FROM [Menus] m JOIN (VALUES (17, 8), (18, 8), (19, 8), (20, 8), (21, 8), (22, 9), (23, 9), (24, 9), (25, 10), (26, 10), (27, 10), (28, 10), (29, 10), (30, 11), (31, 11), (32, 11), (33, 11), (34, 11), (35, 11), (36, 11), (37, 12), (38, 12), (39, 13), (40, 13), (41, 13), (42, 13), (43, 13), (45, 16), (50, 8)) AS p(MenuID, ParentID) ON p.MenuID = m.MenuID;

-- Roles
SET IDENTITY_INSERT [Roles] ON;
INSERT INTO [Roles] ([RoleID], [RoleName], [Description], [IsActive], [CreatedDate], [CreatedBy], [ModifiedDate], [ModifiedBy], [CompanyID]) VALUES
    (5, N'ADMIN', N'Quan tri he thong', 1, '2026-09-23T13:58:58.313', NULL, NULL, NULL, 1),
    (6, N'HR', N'Nhan su', 1, '2026-09-23T13:58:58.313', NULL, NULL, NULL, 1),
    (7, N'MANAGE', N'Quan ly', 1, '2026-09-23T13:58:58.313', NULL, NULL, NULL, 1),
    (8, N'EMPLOYEE', N'Nhan vien', 1, '2026-09-23T13:58:58.313', NULL, NULL, NULL, 1);
SET IDENTITY_INSERT [Roles] OFF;

-- RolePermissions
SET IDENTITY_INSERT [RolePermissions] ON;
INSERT INTO [RolePermissions] ([RolePermissionID], [RoleID], [PermissionID]) VALUES
    (1, 5, 1),
    (2, 5, 2),
    (3, 5, 4),
    (4, 5, 5),
    (5, 5, 6),
    (6, 5, 7),
    (7, 5, 8),
    (8, 5, 10),
    (9, 5, 11),
    (10, 5, 12),
    (11, 5, 14),
    (12, 5, 15),
    (13, 5, 16),
    (14, 5, 18),
    (15, 5, 19),
    (16, 6, 2),
    (17, 6, 4),
    (18, 6, 9),
    (19, 6, 11),
    (20, 6, 12),
    (21, 6, 13),
    (22, 6, 14),
    (23, 6, 16),
    (24, 6, 17),
    (25, 6, 19),
    (41, 7, 12),
    (42, 7, 3),
    (43, 7, 11),
    (44, 7, 15),
    (45, 7, 14),
    (46, 7, 16),
    (47, 7, 8),
    (48, 7, 4),
    (775, 5, 20);
SET IDENTITY_INSERT [RolePermissions] OFF;

-- RoleMenus
INSERT INTO [RoleMenus] ([RoleID], [MenuID]) VALUES
    (5, 1),
    (5, 8),
    (5, 9),
    (5, 10),
    (5, 11),
    (5, 13),
    (5, 15),
    (5, 17),
    (5, 21),
    (5, 22),
    (5, 23),
    (5, 24),
    (5, 28),
    (5, 29),
    (5, 31),
    (5, 32),
    (5, 34),
    (5, 36),
    (5, 39),
    (5, 43),
    (5, 50),
    (6, 2),
    (6, 9),
    (6, 10),
    (6, 11),
    (6, 12),
    (6, 13),
    (6, 15),
    (6, 16),
    (6, 22),
    (6, 23),
    (6, 24),
    (6, 25),
    (6, 26),
    (6, 27),
    (6, 28),
    (6, 29),
    (6, 30),
    (6, 31),
    (6, 32),
    (6, 33),
    (6, 37),
    (6, 38),
    (6, 39),
    (6, 40),
    (6, 41),
    (6, 42),
    (6, 43),
    (6, 45),
    (7, 3),
    (7, 10),
    (7, 11),
    (7, 12),
    (7, 13),
    (7, 15),
    (7, 25),
    (7, 26),
    (7, 30),
    (7, 31),
    (7, 33),
    (7, 34),
    (7, 35),
    (7, 36),
    (7, 37),
    (7, 39),
    (7, 40),
    (7, 41),
    (8, 4),
    (8, 5),
    (8, 6),
    (8, 7),
    (8, 13),
    (8, 15),
    (8, 40),
    (8, 41);

-- LeaveTypes
SET IDENTITY_INSERT [LeaveTypes] ON;
INSERT INTO [LeaveTypes] ([LeaveTypeID], [LeaveTypeCode], [LeaveTypeName], [AnnualEntitlement], [IsPaid], [RequiresApproval], [IsCarryForward], [MaxCarryForwardDays], [IsActive], [CreatedDate], [CreatedBy], [CompanyID]) VALUES
    (1, N'LT-ANNUAL', N'Nghi phep nam', 12, 1, 1, 1, 5, 1, '2026-09-29T10:32:13.577', NULL, 1),
    (2, N'LT-SICK', N'Nghi om', 30, 1, 1, 0, 0, 1, '2026-09-29T10:32:13.577', NULL, 1),
    (3, N'LT-UNPAID', N'Nghi khong luong', 0, 0, 1, 0, 0, 1, '2026-09-29T10:32:13.577', NULL, 1);
SET IDENTITY_INSERT [LeaveTypes] OFF;

-- Shifts
SET IDENTITY_INSERT [Shifts] ON;
INSERT INTO [Shifts] ([ShiftID], [ShiftCode], [ShiftName], [StartTime], [EndTime], [WorkingHours], [ShiftType], [LateGraceMinutes], [EarlyCheckInMinutes], [LatestCheckInMinutes], [EarliestCheckOutMinutes], [LatestCheckOutMinutes], [IsOvernight], [IsActive], [CreatedDate], [CreatedBy], [CompanyID]) VALUES
    (1, N'SHIFT-HC', N'Ca hanh chinh', '08:00:00', '17:00:00', 8, N'Regular', NULL, NULL, NULL, NULL, NULL, NULL, 1, '2026-09-29T10:32:13.577', NULL, 1);
SET IDENTITY_INSERT [Shifts] OFF;

-- PayrollPolicies
SET IDENTITY_INSERT [PayrollPolicies] ON;
INSERT INTO [PayrollPolicies] ([PolicyID], [PolicyName], [PolicyType], [Description], [CalculationFormula], [BaseAmount], [ApplicableEmployeeGroup], [EffectiveStartDate], [EffectiveEndDate], [IsActive], [CreatedDate], [CreatedBy], [ModifiedDate], [ModifiedBy], [CompanyID]) VALUES
    (1, N'Phu cap an trua', N'Allowance', N'Phu cap an trua hang thang', NULL, 730000.00, N'All', '2026-09-29', NULL, 1, '2026-09-29T10:32:13.577', NULL, NULL, NULL, 1),
    (2, N'Phu cap xang xe', N'Allowance', N'Phu cap di lai hang thang', NULL, 300000.00, N'All', '2026-09-29', NULL, 1, '2026-09-29T10:32:13.577', NULL, NULL, NULL, 1);
SET IDENTITY_INSERT [PayrollPolicies] OFF;

-- SystemSettings
SET IDENTITY_INSERT [SystemSettings] ON;
INSERT INTO [SystemSettings] ([SettingID], [SettingKey], [SettingValue], [SettingCategory], [Description], [ModifiedDate], [ModifiedBy], [CompanyID]) VALUES
    (7, N'Payroll.CutOffDay', N'25', N'Payroll', N'Ngay chot cong hang thang', '2026-09-29T10:32:13.577', NULL, 1),
    (8, N'Payroll.DefaultReviewWindowDays', N'5', N'Payroll', N'So ngay ra soat cham cong truoc khi tinh luong', '2026-09-29T10:32:13.577', NULL, 1),
    (9, N'Payroll.Calc.BhxhRate', N'0.08', N'Payroll', N'Ty le BHXH nguoi lao dong', '2026-09-29T10:32:13.577', NULL, 1),
    (10, N'Payroll.Calc.BhytRate', N'0.015', N'Payroll', N'Ty le BHYT nguoi lao dong', '2026-09-29T10:32:13.577', NULL, 1),
    (11, N'Payroll.Calc.BhtnRate', N'0.01', N'Payroll', N'Ty le BHTN nguoi lao dong', '2026-09-29T10:32:13.577', NULL, 1),
    (12, N'Payroll.Calc.InsuranceCap', N'36000000', N'Payroll', N'Muc luong toi da dong bao hiem', '2026-09-29T10:32:13.577', NULL, 1),
    (13, N'Payroll.Calc.InsuranceBaseMode', N'Gross', N'Payroll', N'Co so tinh bao hiem', '2026-09-29T10:32:13.577', NULL, 1),
    (14, N'Payroll.Calc.InsuranceFixedBase', N'0', N'Payroll', N'Muc luong co dinh dong bao hiem (neu co)', '2026-09-29T10:32:13.577', NULL, 1),
    (15, N'Payroll.Calc.PersonalDeduction', N'11000000', N'Payroll', N'Muc giam tru gia canh ban than', '2026-09-29T10:32:13.577', NULL, 1),
    (16, N'Payroll.Calc.DependentDeduction', N'4400000', N'Payroll', N'Muc giam tru gia canh nguoi phu thuoc', '2026-09-29T10:32:13.577', NULL, 1),
    (17, N'Payroll.Calc.OtWeekdayMultiplier', N'1.5', N'Payroll', N'He so tang ca ngay thuong', '2026-09-29T10:32:13.577', NULL, 1),
    (18, N'Payroll.Calc.OtWeekendMultiplier', N'2.0', N'Payroll', N'He so tang ca cuoi tuan', '2026-09-29T10:32:13.577', NULL, 1),
    (19, N'Payroll.Calc.OtHolidayMultiplier', N'3.0', N'Payroll', N'He so tang ca ngay le', '2026-09-29T10:32:13.577', NULL, 1),
    (24, N'ResignationNoticeDays', N'30', N'Resignation', N'So ngay bao truoc khi nghi viec', '2026-09-29T10:32:13.577', NULL, 1);
SET IDENTITY_INSERT [SystemSettings] OFF;

");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM [SystemSettings] WHERE [CompanyID] = 1;
DELETE FROM [PayrollPolicies] WHERE [CompanyID] = 1;
DELETE FROM [Shifts] WHERE [CompanyID] = 1;
DELETE FROM [LeaveTypes] WHERE [CompanyID] = 1;
DELETE FROM [RoleMenus] WHERE [RoleID] IN (SELECT [RoleID] FROM [Roles] WHERE [CompanyID] = 1);
DELETE FROM [RolePermissions] WHERE [RoleID] IN (SELECT [RoleID] FROM [Roles] WHERE [CompanyID] = 1);
DELETE FROM [Roles] WHERE [CompanyID] = 1;
UPDATE [Menus] SET [ParentID] = NULL;
DELETE FROM [Menus];
DELETE FROM [Permissions];
DELETE FROM [Companies] WHERE [CompanyID] = 1;");
        }
    }
}
