using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyDateIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignments_CompanyID",
                table: "ShiftAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignments_EmployeeID",
                table: "ShiftAssignments");

            migrationBuilder.DropIndex(
                name: "IX_Attendance_EmployeeDate",
                table: "AttendanceRecords");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_CompanyID",
                table: "AttendanceRecords");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_Company_Date",
                table: "ShiftAssignments",
                columns: new[] { "CompanyID", "AssignmentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_Employee_Date",
                table: "ShiftAssignments",
                columns: new[] { "EmployeeID", "AssignmentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_Company_Date",
                table: "AttendanceRecords",
                columns: new[] { "CompanyID", "AttendanceDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignments_Company_Date",
                table: "ShiftAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignments_Employee_Date",
                table: "ShiftAssignments");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_Company_Date",
                table: "AttendanceRecords");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_CompanyID",
                table: "ShiftAssignments",
                column: "CompanyID");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_EmployeeID",
                table: "ShiftAssignments",
                column: "EmployeeID");

            migrationBuilder.CreateIndex(
                name: "IX_Attendance_EmployeeDate",
                table: "AttendanceRecords",
                columns: new[] { "EmployeeID", "AttendanceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_CompanyID",
                table: "AttendanceRecords",
                column: "CompanyID");
        }
    }
}
