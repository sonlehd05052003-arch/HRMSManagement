using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRManagement.Migrations
{
    /// <summary>
    /// The SuperAdmin's sidebar moves from code into the menu tree: menus flagged ShowForSuperAdmin are shown
    /// to it. Seeds the company management screen and flags the screens it already had.
    /// </summary>
    public partial class AddSuperAdminMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowForSuperAdmin",
                table: "Menus",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [Menus] WHERE Code = N'platform.companies')
    INSERT INTO [Menus] (ParentID, Code, Title, Route, IconName, DisplayOrder, IsActive)
    VALUES (NULL, N'platform.companies', N'Quản lý công ty', N'/superadmin/companies', N'BankOutlined', 0, 1);

UPDATE [Menus] SET ShowForSuperAdmin = 1
WHERE Code IN (N'platform.companies', N'system', N'system.users', N'system.roles', N'system.permissions', N'system.menus');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE rm FROM [RoleMenus] rm
JOIN [Menus] m ON m.MenuID = rm.MenuID
WHERE m.Code = N'platform.companies';

DELETE FROM [Menus] WHERE Code = N'platform.companies';");

            migrationBuilder.DropColumn(
                name: "ShowForSuperAdmin",
                table: "Menus");
        }
    }
}
