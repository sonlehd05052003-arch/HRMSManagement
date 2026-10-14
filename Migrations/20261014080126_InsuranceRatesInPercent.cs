using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRManagement.Migrations
{
    /// <summary>
    /// The insurance rates are percentages (8 for 8 %), as the settings page and PayrollService use them, but the seeded
    /// template held fractions (0.08), copied to every company: insurance came out a hundred times too small.
    /// A rate under 1 can only be such a fraction, so only those are multiplied.
    /// </summary>
    public partial class InsuranceRatesInPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE [SystemSettings]
SET [SettingValue] = CONVERT(nvarchar(50), CAST(TRY_CAST([SettingValue] AS decimal(9, 6)) * 100 AS float))
WHERE [SettingKey] IN (N'Payroll.Calc.BhxhRate', N'Payroll.Calc.BhytRate', N'Payroll.Calc.BhtnRate')
  AND TRY_CAST([SettingValue] AS decimal(9, 6)) > 0
  AND TRY_CAST([SettingValue] AS decimal(9, 6)) < 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
