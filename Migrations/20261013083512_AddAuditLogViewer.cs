using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogViewer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Permission and menu entry for the audit log page, given to every company's ADMIN role
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE PermissionKey = N'AuditLog.View')
    INSERT INTO [Permissions] ([PermissionKey], [DisplayName], [Category], [DisplayOrder], [IsActive])
    VALUES (N'AuditLog.View', N'Xem nhật ký thay đổi dữ liệu', N'Cấu hình hệ thống', 3, 1);

INSERT INTO [RolePermissions] ([RoleID], [PermissionID])
SELECT r.RoleID, p.PermissionID
FROM [Roles] r
JOIN [Permissions] p ON p.PermissionKey = N'AuditLog.View'
WHERE r.RoleName = N'ADMIN'
  AND NOT EXISTS (SELECT 1 FROM [RolePermissions] x WHERE x.RoleID = r.RoleID AND x.PermissionID = p.PermissionID);

IF NOT EXISTS (SELECT 1 FROM [Menus] WHERE Code = N'system.auditlog')
    INSERT INTO [Menus] (ParentID, Code, Title, Route, IconName, DisplayOrder, IsActive)
    SELECT MenuID, N'system.auditlog', N'Nhật ký thay đổi', N'/admin/audit-log', NULL, 6, 1
    FROM [Menus] WHERE Code = N'system';

INSERT INTO [RoleMenus] ([RoleID], [MenuID])
SELECT r.RoleID, m.MenuID
FROM [Roles] r
JOIN [Menus] m ON m.Code = N'system.auditlog'
WHERE r.RoleName = N'ADMIN'
  AND NOT EXISTS (SELECT 1 FROM [RoleMenus] x WHERE x.RoleID = r.RoleID AND x.MenuID = m.MenuID);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE rm FROM [RoleMenus] rm JOIN [Menus] m ON m.MenuID = rm.MenuID WHERE m.Code = N'system.auditlog';
DELETE FROM [Menus] WHERE Code = N'system.auditlog';
DELETE rp FROM [RolePermissions] rp JOIN [Permissions] p ON p.PermissionID = rp.PermissionID WHERE p.PermissionKey = N'AuditLog.View';
DELETE FROM [Permissions] WHERE PermissionKey = N'AuditLog.View';");
        }
    }
}
