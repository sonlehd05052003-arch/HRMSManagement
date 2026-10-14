using HRManagement.DTOs.SystemSettings;
using HRManagement.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace HRManagement.Services.Payroll
{
    public class PayslipPdfService
    {
        public PayslipPdfService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public byte[] GeneratePdf(PayrollRecord record, PayrollPeriod period, CompanySettingsDto? company = null)
        {
            var companyName = company?.CompanyName ?? "";
            var address     = company?.Address ?? "";

            // ── Calculations ──────────────────────────────────────────────────
            var salariedAmount = record.WorkingDays > 0
                ? Math.Round(record.BaseSalary / record.WorkingDays * record.ActualWorkingDays, 0)
                : 0m;

            var allowances      = record.PayrollAllowances.Where(a => a.AllowanceType != "Overtime").ToList();
            var totalAllowances = allowances.Sum(a => a.Amount);
            var grossPay        = salariedAmount + totalAllowances + record.OvertimePay + record.BonusAmount;

            // Insurance breakdown: BHXH 8%, BHYT 1.5%, BHTN 1% (total 10.5%)
            decimal bhxh = 0m, bhyt = 0m, bhtn = 0m;
            if (record.InsuranceAmount > 0)
            {
                bhxh = Math.Round(record.InsuranceAmount * 8m / 10.5m, 0);
                bhyt = Math.Round(record.InsuranceAmount * 1.5m / 10.5m, 0);
                bhtn = record.InsuranceAmount - bhxh - bhyt;
            }

            var advanceTotal = record.PayrollDeductions
                .Where(d => d.DeductionType == "Advance")
                .Sum(d => d.Amount);
            var otherTotal = record.PayrollDeductions
                .Where(d => d.DeductionType != "Insurance" && d.DeductionType != "Tax" && d.DeductionType != "Advance")
                .Sum(d => d.Amount);

            var totalDeductions = record.InsuranceAmount + record.TaxAmount + advanceTotal + otherTotal;
            var netPay          = grossPay - totalDeductions;

            var bc = Colors.Black;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Content().Column(col =>
                    {
                        // ── 1. Company header ──────────────────────────────────
                        col.Item().PaddingBottom(2).Text($"Công ty: {companyName}").Bold();
                        col.Item().PaddingBottom(2).Text($"Địa chỉ: {address}");
                        col.Item().PaddingBottom(6).Row(r =>
                        {
                            r.AutoItem().Text($"ĐT: {company?.Phone ?? ""}");
                            r.ConstantItem(24);
                            r.AutoItem().Text($"Email: {company?.Email ?? ""}");
                        });

                        col.Item().PaddingBottom(2).AlignCenter().Text("PHIẾU LƯƠNG").Bold().FontSize(14);
                        col.Item().PaddingBottom(6).AlignCenter()
                            .Text($"Ngày {period.EndDate.Day} tháng {period.Month} năm {period.Year}");

                        col.Item().Height(6);

                        // ── 2. Employee info ───────────────────────────────────
                        col.Item().Border(1).BorderColor(bc).Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2.2f);
                                c.RelativeColumn(2.8f);
                                c.RelativeColumn(2.2f);
                                c.RelativeColumn(2.8f);
                            });

                            EmpRow(t, "Mã Nhân Viên",  record.Employee.EmployeeCode,               "Lương đóng BHBB",    Fmt(record.BaseSalary));
                            EmpRow(t, "Họ Và Tên",     record.Employee.FullName,                   "Ngày công đi làm",   record.ActualWorkingDays.ToString("N1", new CultureInfo("vi-VN")));
                            EmpRow(t, "Chức Danh",     record.Employee.Position?.PositionName ?? "", "Ngày công chuẩn",  record.WorkingDays.ToString("N1", new CultureInfo("vi-VN")));
                        });

                        col.Item().Height(6);

                        // ── 3. Income / Deduction table ────────────────────────
                        col.Item().Border(1).BorderColor(bc).Row(row =>
                        {
                            // Left — income
                            row.RelativeItem().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(38);
                                    c.RelativeColumn();
                                    c.ConstantColumn(90);
                                });

                                // Header
                                t.Cell().Border(1).BorderColor(bc).Padding(4).AlignCenter().Text("STT").Bold();
                                t.Cell().Border(1).BorderColor(bc).Padding(4).AlignCenter().Text("Các Khoản Thu Nhập").Bold();
                                t.Cell().Border(1).BorderColor(bc).Padding(4).AlignCenter().Text("").Bold();

                                // 1. Basic salary
                                IncomeRow(t, "1", "Lương Chính", Fmt(salariedAmount));

                                // 2. Allowances
                                if (allowances.Count > 0)
                                {
                                    IncomeRow(t, "2", "Phụ Cấp:", "............");
                                    for (var i = 0; i < allowances.Count; i++)
                                        IncomeRow(t, $"2.{i + 1}", allowances[i].AllowanceName, Fmt(allowances[i].Amount), isIndented: true);
                                }

                                // Overtime
                                if (record.OvertimePay > 0)
                                    IncomeRow(t, allowances.Count > 0 ? "3" : "2", "Lương tăng ca", Fmt(record.OvertimePay));

                                // Bonus
                                if (record.BonusAmount > 0)
                                    IncomeRow(t, "4", "Thưởng", Fmt(record.BonusAmount));

                                // Total row
                                t.Cell().Border(1).BorderColor(bc).Padding(4).Text("");
                                t.Cell().Border(1).BorderColor(bc).Padding(4).Text("Tổng Cộng").Bold();
                                t.Cell().Border(1).BorderColor(bc).Padding(4).AlignRight().Text(Fmt(grossPay)).Bold();
                            });

                            // Vertical divider
                            row.ConstantItem(1).Background(bc);

                            // Right — deductions
                            row.RelativeItem().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(38);
                                    c.RelativeColumn();
                                    c.ConstantColumn(90);
                                });

                                // Header
                                t.Cell().Border(1).BorderColor(bc).Padding(4).AlignCenter().Text("STT").Bold();
                                t.Cell().Border(1).BorderColor(bc).Padding(4).AlignCenter().Text("Các Khoản Trừ Vào Lương").Bold();
                                t.Cell().Border(1).BorderColor(bc).Padding(4).AlignCenter().Text("").Bold();

                                // 1. Insurance
                                DeductionRow(t, "1", "Bảo Hiểm Bắt Buộc", record.InsuranceAmount > 0 ? "............" : "");
                                if (record.InsuranceAmount > 0)
                                {
                                    DeductionRow(t, "1.1", "Bảo hiểm xã hội (8%)",       Fmt(bhxh), isIndented: true);
                                    DeductionRow(t, "1.2", "Bảo hiểm y tế (1,5%)",       Fmt(bhyt), isIndented: true);
                                    DeductionRow(t, "1.3", "Bảo hiểm thất nghiệp (1%)",  Fmt(bhtn), isIndented: true);
                                }

                                // 2. Personal income tax
                                DeductionRow(t, "2", "Thuế Thu Nhập Cá Nhân", record.TaxAmount > 0 ? Fmt(record.TaxAmount) : "");

                                // 3. Advance
                                DeductionRow(t, "3", "Tạm Ứng", advanceTotal > 0 ? Fmt(advanceTotal) : "");

                                // 4. Other
                                DeductionRow(t, "4", "Khác", otherTotal > 0 ? Fmt(otherTotal) : "");

                                // Total row
                                t.Cell().Border(1).BorderColor(bc).Padding(4).Text("");
                                t.Cell().Border(1).BorderColor(bc).Padding(4).Text("Tổng Cộng").Bold();
                                t.Cell().Border(1).BorderColor(bc).Padding(4).AlignRight().Text(Fmt(totalDeductions)).Bold();
                            });
                        });

                        col.Item().Height(6);

                        // ── 4. Net pay ─────────────────────────────────────────
                        col.Item().Border(1).BorderColor(bc).Padding(6).Row(r =>
                        {
                            r.RelativeItem().Text("Tổng Số Tiền Lương Thực Nhận:").Bold().FontSize(11);
                            r.ConstantItem(160).AlignRight().Text(Fmt(netPay)).Bold().FontSize(11);
                        });

                        // ── 5. Signatures ──────────────────────────────────────
                        col.Item().PaddingTop(24).Row(r =>
                        {
                            r.RelativeItem().AlignCenter().Column(c =>
                            {
                                c.Item().AlignCenter().Text("Người lao động").Bold();
                                c.Item().PaddingTop(40).AlignCenter().Text("(Ký, họ tên)").FontSize(9);
                            });
                            r.RelativeItem().AlignCenter().Column(c =>
                            {
                                c.Item().AlignCenter().Text("Giám đốc").Bold();
                                c.Item().PaddingTop(40).AlignCenter().Text("(Ký, họ tên)").FontSize(9);
                            });
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Trang ");
                        x.CurrentPageNumber();
                    });
                });
            });

            return document.GeneratePdf();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void EmpRow(TableDescriptor t, string label1, string val1, string label2, string val2)
        {
            var bc = Colors.Black;
            t.Cell().Border(1).BorderColor(bc).Padding(4).Text(label1).Bold();
            t.Cell().Border(1).BorderColor(bc).Padding(4).Text(val1);
            t.Cell().Border(1).BorderColor(bc).Padding(4).Text(label2).Bold();
            t.Cell().Border(1).BorderColor(bc).Padding(4).Text(val2);
        }

        private static void IncomeRow(TableDescriptor t, string stt, string name, string amount, bool isIndented = false)
        {
            var bc = Colors.Black;
            t.Cell().Border(1).BorderColor(bc).Padding(4).AlignCenter().Text(stt);
            if (isIndented)
                t.Cell().Border(1).BorderColor(bc).PaddingTop(4).PaddingBottom(4).PaddingRight(4).PaddingLeft(14).Text(name);
            else
                t.Cell().Border(1).BorderColor(bc).Padding(4).Text(name);
            t.Cell().Border(1).BorderColor(bc).Padding(4).AlignRight().Text(amount);
        }

        private static void DeductionRow(TableDescriptor t, string stt, string name, string amount, bool isIndented = false)
        {
            var bc = Colors.Black;
            t.Cell().Border(1).BorderColor(bc).Padding(4).AlignCenter().Text(stt);
            if (isIndented)
                t.Cell().Border(1).BorderColor(bc).PaddingTop(4).PaddingBottom(4).PaddingRight(4).PaddingLeft(14).Text(name);
            else
                t.Cell().Border(1).BorderColor(bc).Padding(4).Text(name);
            t.Cell().Border(1).BorderColor(bc).Padding(4).AlignRight().Text(amount);
        }

        private static string Fmt(decimal amount)
            => amount.ToString("N0", new CultureInfo("vi-VN"));
    }
}
