using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRManagement.Migrations
{
    /// <summary>The HR reports page: its permission and menu, given to ADMIN and HR in every company (the template included).</summary>
    public partial class AddHrReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE PermissionKey = N'Reports.View')
    INSERT INTO [Permissions] ([PermissionKey], [DisplayName], [Category], [DisplayOrder], [IsActive])
    VALUES (N'Reports.View', N'Xem báo cáo nhân sự (biến động, chấm công, chi phí lương)', N'Báo cáo', 8, 1);

INSERT INTO [RolePermissions] ([RoleID], [PermissionID])
SELECT r.RoleID, p.PermissionID
FROM [Roles] r
JOIN [Permissions] p ON p.PermissionKey = N'Reports.View'
WHERE r.RoleName IN (N'ADMIN', N'HR')
  AND NOT EXISTS (SELECT 1 FROM [RolePermissions] x WHERE x.RoleID = r.RoleID AND x.PermissionID = p.PermissionID);

IF NOT EXISTS (SELECT 1 FROM [Menus] WHERE Code = N'reports')
    INSERT INTO [Menus] (ParentID, Code, Title, Route, IconName, DisplayOrder, IsActive, ShowForSuperAdmin)
    VALUES (NULL, N'reports', N'Báo cáo nhân sự', N'/reports/hr', N'BarChartOutlined', 70, 1, 0);

INSERT INTO [RoleMenus] ([RoleID], [MenuID])
SELECT r.RoleID, m.MenuID
FROM [Roles] r
JOIN [Menus] m ON m.Code = N'reports'
WHERE r.RoleName IN (N'ADMIN', N'HR')
  AND NOT EXISTS (SELECT 1 FROM [RoleMenus] x WHERE x.RoleID = r.RoleID AND x.MenuID = m.MenuID);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE rm FROM [RoleMenus] rm JOIN [Menus] m ON m.MenuID = rm.MenuID WHERE m.Code = N'reports';
DELETE FROM [Menus] WHERE Code = N'reports';
DELETE rp FROM [RolePermissions] rp JOIN [Permissions] p ON p.PermissionID = rp.PermissionID WHERE p.PermissionKey = N'Reports.View';
DELETE FROM [Permissions] WHERE PermissionKey = N'Reports.View';");
        }
    }
}
