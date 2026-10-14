using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRManagement.Migrations
{
    /// <summary>
    /// GrossPay and NetPay were computed columns, so what PayrollService calculated was thrown away: gross ignored the
    /// days actually worked, and net took tax and insurance off twice (TotalDeductions already holds them). They become
    /// plain columns. Existing rows keep their gross; their net is corrected to gross minus total deductions.
    /// Recalculating a period that is not approved yet also brings its gross in line with the days worked.
    /// </summary>
    public partial class StorePayrollTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE [PayrollRecords] ADD [GrossPayStored] decimal(21,2) NULL, [NetPayStored] decimal(24,2) NULL;");
            migrationBuilder.Sql("UPDATE [PayrollRecords] SET [GrossPayStored] = [GrossPay], [NetPayStored] = [GrossPay] - [TotalDeductions];");
            migrationBuilder.Sql("ALTER TABLE [PayrollRecords] DROP COLUMN [NetPay], [GrossPay];");
            migrationBuilder.Sql("EXEC sp_rename N'[PayrollRecords].[GrossPayStored]', N'GrossPay', N'COLUMN';");
            migrationBuilder.Sql("EXEC sp_rename N'[PayrollRecords].[NetPayStored]', N'NetPay', N'COLUMN';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE [PayrollRecords] DROP COLUMN [NetPay], [GrossPay];");
            migrationBuilder.Sql(@"ALTER TABLE [PayrollRecords] ADD
    [GrossPay] AS ((([BaseSalary]+[TotalAllowances])+[OvertimePay])+[BonusAmount]) PERSISTED,
    [NetPay] AS (((((([BaseSalary]+[TotalAllowances])+[OvertimePay])+[BonusAmount])-[TotalDeductions])-[TaxAmount])-[InsuranceAmount]) PERSISTED;");
        }
    }
}
