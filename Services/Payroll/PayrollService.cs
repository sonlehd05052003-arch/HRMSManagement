using System.Globalization;
using AutoMapper;
using ClosedXML.Excel;
using HRManagement.DataAcess.Interfaces;
using HRManagement.DTOs.Payroll;
using HRManagement.Models;
using HRManagement.Services.Emails;
using HRManagement.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Services.Payroll
{
    public class PayrollService : IPayrollService
    {
        private readonly IPayrollRepository _payrollRepo;
        private readonly IPayrollPeriodRepository _periodRepo;
        private readonly TaxCalculationService _taxService;
        private readonly HrmsDbContext _context;
        private readonly IMapper _mapper;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notifications;

        public PayrollService(
            IPayrollRepository payrollRepo,
            IPayrollPeriodRepository periodRepo,
            TaxCalculationService taxService,
            HrmsDbContext context,
            IMapper mapper,
            IEmailService emailService,
            INotificationService notifications)
        {
            _payrollRepo = payrollRepo;
            _periodRepo = periodRepo;
            _taxService = taxService;
            _context = context;
            _mapper = mapper;
            _emailService = emailService;
            _notifications = notifications;
        }

        // ── Kỳ lương ──────────────────────────────────────────────────────────
        public async Task<PayrollPeriodDto> CreatePeriodAsync(CreatePayrollPeriodDto dto)
        {
            if (await _periodRepo.ExistsAsync(dto.Month, dto.Year))
                throw new InvalidOperationException($"Kỳ lương {dto.Month}/{dto.Year} đã tồn tại.");

            var period = new PayrollPeriod
            {
                Month = dto.Month,
                Year = dto.Year,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                Status = "Open",
                AttendanceCutoffDate = dto.AttendanceCutoffDate,
                ReviewWindowDays = dto.ReviewWindowDays,
            };
            await _periodRepo.CreateAsync(period);
            return _mapper.Map<PayrollPeriodDto>(period);
        }

        public async Task<List<PayrollPeriodDto>> GetAllPeriodsAsync()
        {
            var periods = await _periodRepo.GetAllAsync();
            return _mapper.Map<List<PayrollPeriodDto>>(periods);
        }

        public async Task<PayrollPeriodDto> GetPeriodByIdAsync(int periodId)
        {
            var period = await _periodRepo.GetByIdAsync(periodId)
                ?? throw new KeyNotFoundException($"Không tìm thấy kỳ lương {periodId}.");

            var dto = _mapper.Map<PayrollPeriodDto>(period);
            dto.ReviewDeadline        = period.ReviewDeadline;
            dto.ReviewDeadlineExpired = period.ReviewDeadline.HasValue && AppTime.Now > period.ReviewDeadline.Value;
            return dto;
        }

        public async Task<PayrollSummaryDto> GetPeriodSummaryAsync(int periodId)
        {
            var period = await _periodRepo.GetByIdAsync(periodId)
                ?? throw new KeyNotFoundException("Không tìm thấy kỳ lương.");

            var records = await _payrollRepo.GetByPeriodAsync(periodId);

            var summary = new PayrollSummaryDto
            {
                PeriodId = periodId,
                Month = period.Month,
                Year = period.Year,
                TotalEmployees = records.Count,
                TotalBaseSalary = records.Sum(r => r.BaseSalary),
                TotalAllowances = records.Sum(r => r.TotalAllowances),
                TotalOvertimePay = records.Sum(r => r.OvertimePay),
                TotalBonuses = records.Sum(r => r.BonusAmount),
                TotalGrossPay = records.Sum(r => r.GrossPay ?? 0m),
                TotalInsurance = records.Sum(r => r.InsuranceAmount),
                TotalTax = records.Sum(r => r.TaxAmount),
                TotalDeductions = records.Sum(r => r.TotalDeductions),
                TotalNetPay = records.Sum(r => r.NetPay ?? 0m)
            };

            summary.ByDepartment = records
                .GroupBy(r => r.Employee.Department?.DepartmentName ?? "Unknown")
                .Select(g => new DepartmentPayrollSummary
                {
                    DepartmentName = g.Key,
                    EmployeeCount = g.Count(),
                    TotalNetPay = g.Sum(r => r.NetPay ?? 0m)
                })
                .ToList();

            return summary;
        }

        // ── Helper: load cấu hình tính lương từ SystemSettings ───────────────
        private async Task<HRManagement.DTOs.SystemSettings.PayrollCalculationSettingsDto> LoadPayrollCalcSettingsAsync()
        {
            var keys = new[]
            {
                "Payroll.Calc.BhxhRate", "Payroll.Calc.BhytRate", "Payroll.Calc.BhtnRate",
                "Payroll.Calc.InsuranceCap", "Payroll.Calc.InsuranceBaseMode", "Payroll.Calc.InsuranceFixedBase",
                "Payroll.Calc.PersonalDeduction", "Payroll.Calc.DependentDeduction",
                "Payroll.Calc.OtWeekdayMultiplier", "Payroll.Calc.OtWeekendMultiplier", "Payroll.Calc.OtHolidayMultiplier"
            };
            var rows = await _context.SystemSettings
                .Where(s => keys.Contains(s.SettingKey))
                .ToListAsync();

            var cfg = new HRManagement.DTOs.SystemSettings.PayrollCalculationSettingsDto();
            foreach (var s in rows)
            {
                if (s.SettingKey == "Payroll.Calc.BhxhRate"              && decimal.TryParse(s.SettingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var v1))  cfg.BhxhRate             = v1;
                if (s.SettingKey == "Payroll.Calc.BhytRate"              && decimal.TryParse(s.SettingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var v2))  cfg.BhytRate             = v2;
                if (s.SettingKey == "Payroll.Calc.BhtnRate"              && decimal.TryParse(s.SettingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var v3))  cfg.BhtnRate             = v3;
                if (s.SettingKey == "Payroll.Calc.InsuranceCap"          && decimal.TryParse(s.SettingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var v4))  cfg.InsuranceCap         = v4;
                if (s.SettingKey == "Payroll.Calc.InsuranceBaseMode")                                                       cfg.InsuranceBaseMode    = s.SettingValue ?? "Gross";
                if (s.SettingKey == "Payroll.Calc.InsuranceFixedBase"    && decimal.TryParse(s.SettingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var v6))  cfg.InsuranceFixedBase   = v6;
                if (s.SettingKey == "Payroll.Calc.PersonalDeduction"     && decimal.TryParse(s.SettingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var v7))  cfg.PersonalDeduction    = v7;
                if (s.SettingKey == "Payroll.Calc.DependentDeduction"    && decimal.TryParse(s.SettingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var v8))  cfg.DependentDeduction   = v8;
                if (s.SettingKey == "Payroll.Calc.OtWeekdayMultiplier"   && decimal.TryParse(s.SettingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var v9))  cfg.OtWeekdayMultiplier  = v9;
                if (s.SettingKey == "Payroll.Calc.OtWeekendMultiplier"   && decimal.TryParse(s.SettingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var v10)) cfg.OtWeekendMultiplier  = v10;
                if (s.SettingKey == "Payroll.Calc.OtHolidayMultiplier"   && decimal.TryParse(s.SettingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var v11)) cfg.OtHolidayMultiplier  = v11;
            }
            return cfg;
        }

        // ── Tính lương ────────────────────────────────────────────────────────
        public async Task<PayrollRecordDto> CalculateForEmployeeAsync(int employeeId, int periodId)
        {
            var period = await _periodRepo.GetByIdAsync(periodId)
                ?? throw new KeyNotFoundException("Không tìm thấy kỳ lương.");

            if (period.Status == "Approved")
                throw new InvalidOperationException("Kỳ lương đã được duyệt, không thể tính lại.");

            if (period.ReviewDeadline.HasValue && AppTime.Now < period.ReviewDeadline.Value)
                throw new InvalidOperationException("Chưa hết hạn review chấm công, chưa thể tính lương.");

            var employee = await _context.Employees
                .Include(e => e.Department)
                .Include(e => e.Position)
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId)
                ?? throw new KeyNotFoundException("Không tìm thấy nhân viên.");

            if (employee.JoinDate > period.EndDate)
                throw new InvalidOperationException($"Nhân viên chưa gia nhập công ty trong kỳ lương này (Ngày vào làm: {employee.JoinDate:dd/MM/yyyy}).");

            // Load cấu hình tính lương từ SystemSettings (có fallback về mặc định)
            var calcSettings = await LoadPayrollCalcSettingsAsync();

            // 1. Tính ngày công
            var workingDays = await CalculateAssignedWorkingDaysAsync(employeeId, period);
            var actualWorkingDays = await CalculateActualWorkingDaysAsync(employeeId, period);

            // 2. Lương theo ngày công thực tế
            var baseSalary = employee.BaseSalary ?? 0m;
            var dailySalary = workingDays > 0 ? baseSalary / workingDays : 0m;
            var salariedAmount = Math.Round(dailySalary * actualWorkingDays, 0);

            // 3. Phụ cấp chính sách
            var allowances = await BuildAllowancesAsync(employee, period);

            // 4. Lương OT
            var (overtimePay, otAllowances) = await CalculateOvertimeAsync(employeeId, period, baseSalary, workingDays, calcSettings);

            // 5. Thưởng & Khấu trừ thủ công từ record cũ (nếu có)
            var existingRecord = await _payrollRepo.GetByEmployeeAndPeriodAsync(employeeId, periodId);
            var bonusAmount = existingRecord?.BonusAmount ?? 0m;

            // TotalAllowances chỉ gồm phụ cấp chính sách (KHÔNG gộp OT)
            var policyAllowancesTotal = allowances.Sum(a => a.Amount);
            var grossPay = salariedAmount + policyAllowancesTotal + overtimePay + bonusAmount;

            // 6 & 7. Bảo hiểm + Thuế TNCN
            // Miễn BH và thuế khi:
            //   - Được phân ca < 14 ngày (nhân viên mới vào/nghỉ giữa tháng)
            //   - Hoặc vắng không lương >= 14 ngày (Điều 85 Luật BHXH 2014)
            var insuranceRate = (calcSettings.BhxhRate + calcSettings.BhytRate + calcSettings.BhtnRate) / 100m;
            var unpaidAbsenceDays = workingDays - (int)Math.Floor(actualWorkingDays);
            bool skipDeductions = workingDays < 14 || unpaidAbsenceDays >= 14;

            decimal insuranceAmount;
            decimal taxAmount;
            int taxBracket = 0;
            if (skipDeductions)
            {
                insuranceAmount = 0m;
                taxAmount = 0m;
            }
            else
            {
                decimal insuranceBase;
                if (employee.InsuranceSalary.HasValue && employee.InsuranceSalary.Value > 0)
                    insuranceBase = Math.Min(employee.InsuranceSalary.Value, calcSettings.InsuranceCap);
                else
                    insuranceBase = Math.Min(employee.BaseSalary ?? 0m, calcSettings.InsuranceCap);
                insuranceAmount = Math.Round(insuranceBase * insuranceRate, 0);

                var taxResult = _taxService.Calculate(
                    grossPay,
                    numberOfDependents: employee.NumberOfDependents,
                    isInsuranceApplicable: true,
                    insuranceAmount: insuranceAmount,
                    personalDeduction: calcSettings.PersonalDeduction,
                    dependentDeduction: calcSettings.DependentDeduction);
                taxAmount = taxResult.TaxAmount;
                taxBracket = taxResult.TaxBracket;
            }

            // 8. Phụ cấp & khấu trừ thủ công (giữ nguyên nếu tính lại)
            var manualAllowances = existingRecord?.PayrollAllowances
                .Where(a => a.AllowanceType == "Manual")
                .Select(a => new PayrollAllowance {
                    AllowanceType = a.AllowanceType,
                    AllowanceName = a.AllowanceName,
                    Amount = a.Amount,
                    Description = a.Description
                })
                .ToList() ?? new List<PayrollAllowance>();

            var manualDeductions = existingRecord?.PayrollDeductions
                .Where(d => d.DeductionType != "Insurance" && d.DeductionType != "Tax")
                .Select(d => new PayrollDeduction {
                    DeductionType = d.DeductionType,
                    DeductionName = d.DeductionName,
                    Amount = d.Amount,
                    Description = d.Description
                })
                .ToList() ?? new List<PayrollDeduction>();

            var totalDeductions = insuranceAmount + taxAmount + manualDeductions.Sum(d => d.Amount);
            var netPay = grossPay - totalDeductions;

            // 9. Upsert record
            var record = existingRecord ?? new PayrollRecord();
            record.EmployeeId = employeeId;
            record.PeriodId = periodId;
            record.BaseSalary = baseSalary;
            record.WorkingDays = workingDays;
            record.ActualWorkingDays = actualWorkingDays;
            record.TotalAllowances = policyAllowancesTotal;  // chỉ phụ cấp chính sách
            record.OvertimePay = overtimePay;                // OT riêng biệt
            record.BonusAmount = bonusAmount;
            record.GrossPay = grossPay;                      // Gross = ngày công + phụ cấp + OT + thưởng
            record.InsuranceAmount = insuranceAmount;
            record.TaxAmount = taxAmount;
            record.TotalDeductions = totalDeductions;
            record.NetPay = netPay;
            record.Status = "Calculated";
            record.CalculatedDate = AppTime.Now;

            // Xử lý quan hệ 1-n: Cách an toàn nhất là xóa n và add lại
            if (existingRecord != null)
            {
                _context.PayrollAllowances.RemoveRange(existingRecord.PayrollAllowances);
                _context.PayrollDeductions.RemoveRange(existingRecord.PayrollDeductions);
                await _context.SaveChangesAsync(); // Lưu để xóa triệt để trước khi add mới
            }

            record.PayrollAllowances = allowances.Concat(otAllowances).Concat(manualAllowances).ToList();
            record.PayrollDeductions = manualDeductions;
            record.PayrollDeductions.Add(new PayrollDeduction
            {
                DeductionType = "Insurance",
                DeductionName = $"BHXH + BHYT + BHTN ({calcSettings.BhxhRate + calcSettings.BhytRate + calcSettings.BhtnRate}%)",
                Amount = insuranceAmount,
            });
            record.PayrollDeductions.Add(new PayrollDeduction
            {
                DeductionType = "Tax",
                DeductionName = taxBracket > 0 ? $"Thuế TNCN (Bậc {taxBracket})" : "Thuế TNCN",
                Amount = taxAmount,
            });

            if (existingRecord == null)
                await _payrollRepo.CreateAsync(record);
            else
                await _payrollRepo.UpdateAsync(record);

            return _mapper.Map<PayrollRecordDto>(record);
        }

        public async Task<List<PayrollRecordDto>> CalculateForAllEmployeesAsync(int periodId)
        {
            var period = await _context.PayrollPeriods.FindAsync(periodId)
                ?? throw new KeyNotFoundException("Không tìm thấy kỳ lương.");

            if (period.Status == "Approved")
                throw new InvalidOperationException("Kỳ lương đã được duyệt, không thể tính lại.");

            if (period.ReviewDeadline.HasValue && AppTime.Now < period.ReviewDeadline.Value)
                throw new InvalidOperationException("Chưa hết hạn review chấm công, chưa thể tính lương.");

            var employees = await _context.Employees
                .Where(e => e.EmploymentStatus == "Active" && e.JoinDate <= period.EndDate)
                .Select(e => e.EmployeeId)
                .ToListAsync();

            var results = await CalculateBatchAsync(periodId, employees);

            // Dùng FindAsync lại để tránh EF Core tracking conflict
            var periodToUpdate = await _context.PayrollPeriods.FindAsync(periodId);
            if (periodToUpdate != null && periodToUpdate.Status != "Approved")
            {
                periodToUpdate.Status = "Calculated";
                periodToUpdate.CalculatedDate = AppTime.Now;
                await _context.SaveChangesAsync();
            }

            return results;
        }

        public async Task<List<PayrollRecordDto>> CalculateBatchAsync(int periodId, List<int> employeeIds)
        {
            var results = new List<PayrollRecordDto>();
            foreach (var empId in employeeIds)
            {
                try
                {
                    var result = await CalculateForEmployeeAsync(empId, periodId);
                    results.Add(result);
                }
                catch (Exception)
                {
                    // Tiếp tục với nhân viên khác khi có lỗi đơn lẻ
                }
            }
            return results;
        }

        // ── Lấy dữ liệu ───────────────────────────────────────────────────────
        public async Task<List<PayrollRecordDto>> GetRecordsByPeriodAsync(int periodId, int? managerEmployeeId = null)
        {
            var records = await _payrollRepo.GetByPeriodWithDetailsAsync(periodId, managerEmployeeId);
            return _mapper.Map<List<PayrollRecordDto>>(records);
        }

        public async Task<PayrollRecordDto> GetRecordByIdAsync(int payrollRecordId)
        {
            var record = await _payrollRepo.GetByIdAsync(payrollRecordId)
                ?? throw new KeyNotFoundException("Không tìm thấy bản ghi lương.");
            return _mapper.Map<PayrollRecordDto>(record);
        }

        public async Task<List<PayrollRecordDto>> GetRecordsByEmployeeAsync(int employeeId)
        {
            var records = await _payrollRepo.GetByEmployeeAsync(employeeId);
            return _mapper.Map<List<PayrollRecordDto>>(records);
        }

        // ── Điều chỉnh thủ công ───────────────────────────────────────────────
        public async Task<PayrollRecordDto> AddAllowanceAsync(int payrollRecordId, CreatePayrollAllowanceDto dto)
        {
            var record = await _payrollRepo.GetByIdAsync(payrollRecordId) ?? throw new KeyNotFoundException();
            record.PayrollAllowances.Add(new PayrollAllowance {
                AllowanceType = dto.AllowanceType,
                AllowanceName = dto.AllowanceName,
                Amount = dto.Amount,
                Description = dto.Description
            });
            record.TotalAllowances += dto.Amount;
            record.GrossPay += dto.Amount;
            record.NetPay += dto.Amount;
            await _payrollRepo.UpdateAsync(record);
            return _mapper.Map<PayrollRecordDto>(record);
        }

        public async Task<PayrollRecordDto> RemoveAllowanceAsync(int payrollRecordId, int allowanceId)
        {
            var record = await _payrollRepo.GetByIdAsync(payrollRecordId) ?? throw new KeyNotFoundException();
            var item = record.PayrollAllowances.FirstOrDefault(a => a.AllowanceId == allowanceId);
            if (item != null)
            {
                record.TotalAllowances -= item.Amount;
                record.GrossPay -= item.Amount;
                record.NetPay -= item.Amount;
                _context.PayrollAllowances.Remove(item);
                await _payrollRepo.UpdateAsync(record);
            }
            return _mapper.Map<PayrollRecordDto>(record);
        }

        public async Task<PayrollRecordDto> AddDeductionAsync(int payrollRecordId, CreatePayrollDeductionDto dto)
        {
            var record = await _payrollRepo.GetByIdAsync(payrollRecordId) ?? throw new KeyNotFoundException();
            record.PayrollDeductions.Add(new PayrollDeduction {
                DeductionType = dto.DeductionType,
                DeductionName = dto.DeductionName,
                Amount = dto.Amount,
                Description = dto.Description
            });
            record.TotalDeductions += dto.Amount;
            record.NetPay -= dto.Amount; 
            await _payrollRepo.UpdateAsync(record);
            return _mapper.Map<PayrollRecordDto>(record);
        }

        public async Task<PayrollRecordDto> RemoveDeductionAsync(int payrollRecordId, int deductionId)
        {
            var record = await _payrollRepo.GetByIdAsync(payrollRecordId) ?? throw new KeyNotFoundException();
            var item = record.PayrollDeductions.FirstOrDefault(d => d.DeductionId == deductionId);
            if (item != null)
            {
                record.TotalDeductions -= item.Amount;
                record.NetPay += item.Amount;
                _context.PayrollDeductions.Remove(item);
                await _payrollRepo.UpdateAsync(record);
            }
            return _mapper.Map<PayrollRecordDto>(record);
        }

        public async Task<PayrollRecordDto> UpdateBonusAsync(int payrollRecordId, decimal bonusAmount)
        {
            var record = await _payrollRepo.GetByIdAsync(payrollRecordId) ?? throw new KeyNotFoundException();
            var diff = bonusAmount - record.BonusAmount;
            record.BonusAmount = bonusAmount;
            record.GrossPay += diff;
            record.NetPay += diff;
            await _payrollRepo.UpdateAsync(record);
            return _mapper.Map<PayrollRecordDto>(record);
        }

        // ── Phê duyệt ─────────────────────────────────────────────────────────
        public async Task<PayrollPeriodDto> ApprovePeriodAsync(int periodId, int approvedByUserId)
        {
            var period = await _periodRepo.GetByIdAsync(periodId)
                ?? throw new KeyNotFoundException("Không tìm thấy kỳ lương.");

            if (period.Status != "Calculated")
                throw new InvalidOperationException("Chỉ có thể phê duyệt kỳ lương đang ở trạng thái Đã tính lương.");

            period.Status = "Approved";
            period.ApprovedDate = AppTime.Now;
            period.ApprovedBy = approvedByUserId;
            await _periodRepo.UpdateAsync(period);

            // Tự động tạo phiếu lương cho toàn bộ nhân viên trong kỳ
            var records = await _payrollRepo.GetByPeriodAsync(periodId);

            // Khoá toàn bộ bản ghi chấm công trong kỳ lương này
            var employeeIds = records.Select(r => r.EmployeeId).ToHashSet();
            var attendanceToLock = await _context.AttendanceRecords
                .Where(a => employeeIds.Contains(a.EmployeeId) &&
                            a.AttendanceDate >= period.StartDate &&
                            a.AttendanceDate <= period.EndDate)
                .ToListAsync();
            foreach (var att in attendanceToLock)
            {
                att.IsLocked = true;
                att.ModifiedDate = AppTime.Now;
            }
            foreach (var record in records)
            {
                var existing = await _context.Payslips
                    .FirstOrDefaultAsync(p => p.PayrollRecordId == record.PayrollRecordId);
                if (existing != null) continue; // Đã có rồi, bỏ qua

                _context.Payslips.Add(new Payslip
                {
                    PayrollRecordId = record.PayrollRecordId,
                    EmployeeId      = record.EmployeeId,
                    PeriodId        = periodId,
                    PayslipNumber   = $"PS-{period.Year}{period.Month:D2}-{record.PayrollRecordId:D5}",
                    GeneratedDate   = AppTime.Now,
                    IsViewed        = false,
                });
            }
            await _context.SaveChangesAsync();

            return _mapper.Map<PayrollPeriodDto>(period);
        }

        public async Task<PayrollPeriodDto> RejectPeriodAsync(int periodId, int rejectedByUserId, string reason)
        {
            var period = await _periodRepo.GetByIdAsync(periodId)
                ?? throw new KeyNotFoundException("Không tìm thấy kỳ lương.");

            if (period.Status != "Calculated")
                throw new InvalidOperationException("Chỉ có thể từ chối kỳ lương đang ở trạng thái Đã tính lương.");

            period.Status          = "Rejected";
            period.RejectionReason = reason;
            period.RejectedBy      = rejectedByUserId;
            period.RejectedDate    = AppTime.Now;
            await _periodRepo.UpdateAsync(period);

            // Xoá toàn bộ feedback và payslip cũ để nhân viên feedback lại sau khi HR tính toán lại
            var payrollRecordIds = await _context.PayrollRecords
                .Where(r => r.PeriodId == periodId)
                .Select(r => r.PayrollRecordId)
                .ToListAsync();

            if (payrollRecordIds.Any())
            {
                var oldFeedbacks = await _context.PayrollFeedbacks
                    .Where(f => payrollRecordIds.Contains(f.PayrollRecordId))
                    .ToListAsync();
                _context.PayrollFeedbacks.RemoveRange(oldFeedbacks);

                var oldPayslips = await _context.Payslips
                    .Where(p => p.PeriodId == periodId)
                    .ToListAsync();
                _context.Payslips.RemoveRange(oldPayslips);

                await _context.SaveChangesAsync();
            }

            var rejector = await _context.Users
                .Include(u => u.Employee)
                .FirstOrDefaultAsync(u => u.UserId == rejectedByUserId);
            var dto = _mapper.Map<PayrollPeriodDto>(period);
            dto.RejectedByName  = rejector?.Employee?.FullName ?? rejector?.Username;
            dto.RejectionReason = reason;
            dto.RejectedDate    = period.RejectedDate;
            return dto;
        }

        public async Task<int> LockAttendanceForAllApprovedPeriodsAsync()
        {
            var approvedPeriods = await _context.PayrollPeriods
                .Where(p => p.Status == "Approved")
                .ToListAsync();

            int totalLocked = 0;
            foreach (var period in approvedPeriods)
            {
                var employeeIds = await _context.PayrollRecords
                    .Where(r => r.PeriodId == period.PeriodId)
                    .Select(r => r.EmployeeId)
                    .ToListAsync();

                var toLock = await _context.AttendanceRecords
                    .Where(a => employeeIds.Contains(a.EmployeeId) &&
                                a.AttendanceDate >= period.StartDate &&
                                a.AttendanceDate <= period.EndDate &&
                                (a.IsLocked == null || a.IsLocked == false))
                    .ToListAsync();

                foreach (var att in toLock)
                {
                    att.IsLocked = true;
                    att.ModifiedDate = AppTime.Now;
                }
                totalLocked += toLock.Count;
            }

            await _context.SaveChangesAsync();
            return totalLocked;
        }

        public async Task<PayrollRecordDto> ApproveRecordAsync(int payrollRecordId, int approvedByUserId)
        {
            var record = await _payrollRepo.GetByIdAsync(payrollRecordId) ?? throw new KeyNotFoundException();
            record.Status = "Approved";
            record.ApprovedDate = AppTime.Now;
            // Thuộc tính ApprovedBy trong PayrollRecord model nếu có
            await _payrollRepo.UpdateAsync(record);
            return _mapper.Map<PayrollRecordDto>(record);
        }

        public async Task<int> GeneratePayslipsForPeriodAsync(int periodId)
        {
            var period = await _periodRepo.GetByIdAsync(periodId)
                ?? throw new KeyNotFoundException("Không tìm thấy kỳ lương.");

            var records = await _payrollRepo.GetByPeriodAsync(periodId);
            int created = 0;
            var newPayslipEmployees = new List<int>();

            foreach (var record in records)
            {
                var existing = await _context.Payslips
                    .FirstOrDefaultAsync(p => p.PayrollRecordId == record.PayrollRecordId);
                if (existing != null) continue;

                _context.Payslips.Add(new Payslip
                {
                    PayrollRecordId = record.PayrollRecordId,
                    EmployeeId      = record.EmployeeId,
                    PeriodId        = periodId,
                    PayslipNumber   = $"PS-{period.Year}{period.Month:D2}-{record.PayrollRecordId:D5}",
                    GeneratedDate   = AppTime.Now,
                    IsViewed        = false,
                });
                newPayslipEmployees.Add(record.EmployeeId);
                created++;
            }

            // Every employee with a new payslip is told it is ready
            var accounts = await _context.Users
                .Where(u => u.EmployeeId != null && newPayslipEmployees.Contains(u.EmployeeId.Value) && u.IsActive)
                .Select(u => u.UserId)
                .ToListAsync();
            foreach (var userId in accounts)
                _notifications.Notify(userId, NotificationTypes.Payslip, "Phiếu lương đã có",
                    $"Phiếu lương tháng {period.Month}/{period.Year} của bạn đã sẵn sàng.",
                    "PayrollPeriod", periodId);

            await _context.SaveChangesAsync();
            return created;
        }

        public async Task<PayslipDto> GeneratePayslipAsync(int payrollRecordId)
        {
            var record = await _payrollRepo.GetByIdAsync(payrollRecordId)
                ?? throw new KeyNotFoundException("Không tìm thấy bản ghi lương.");

            var period = await _periodRepo.GetByIdAsync(record.PeriodId)
                ?? throw new KeyNotFoundException();

            // 1. Kiểm tra Payslip đã tồn tại chưa
            var existingPayslip = await _context.Payslips.FirstOrDefaultAsync(p => p.PayrollRecordId == payrollRecordId);
            
            var payslip = existingPayslip ?? new Payslip();
            payslip.PayrollRecordId = payrollRecordId;
            payslip.EmployeeId = record.EmployeeId;
            payslip.PeriodId = record.PeriodId;
            payslip.PayslipNumber = $"PS-{period.Year}{period.Month:D2}-{record.PayrollRecordId:D5}";
            payslip.GeneratedDate = AppTime.Now;
            payslip.IsViewed = false;

            if (existingPayslip == null)
                _context.Payslips.Add(payslip);
            else
                _context.Payslips.Update(payslip);

            await _context.SaveChangesAsync();
            return _mapper.Map<PayslipDto>(payslip);
        }

        public async Task<byte[]> GetPayslipPdfAsync(int payslipId)
        {
            var payslip = await _context.Payslips
                .Include(p => p.PayrollRecord).ThenInclude(r => r.PayrollAllowances)
                .Include(p => p.PayrollRecord).ThenInclude(r => r.PayrollDeductions)
                .Include(p => p.PayrollRecord).ThenInclude(r => r.Employee).ThenInclude(e => e.Department)
                .Include(p => p.PayrollRecord).ThenInclude(r => r.Employee).ThenInclude(e => e.Position)
                .Include(p => p.Period)
                .FirstOrDefaultAsync(p => p.PayslipId == payslipId)
                ?? throw new KeyNotFoundException();

            // Thông tin công ty in trên phiếu lương lấy từ công ty sở hữu phiếu lương
            var company = await _context.Companies.AsNoTracking()
                .FirstAsync(c => c.CompanyId == payslip.CompanyId);
            var companySettings = HRManagement.DTOs.SystemSettings.CompanySettingsDto.FromCompany(company);

            var pdfService = new PayslipPdfService();
            return pdfService.GeneratePdf(payslip.PayrollRecord, payslip.Period, companySettings);
        }

        public async Task<byte[]> ExportPayrollExcelAsync(int periodId)
        {
            var period = await _periodRepo.GetByIdAsync(periodId) ?? throw new KeyNotFoundException();
            var records = await _payrollRepo.GetByPeriodWithDetailsAsync(periodId);
            
            var excelService = new PayrollExportService();
            return excelService.ExportPayrollExcel(records, period);
        }

        public async Task<List<PayslipDto>> GetPayslipsByEmployeeAsync(int employeeId)
        {
            var payslips = await _context.Payslips
                .Include(p => p.Period)
                .Include(p => p.PayrollRecord)
                    .ThenInclude(r => r.PayrollDeductions)
                .Include(p => p.Employee)
                    .ThenInclude(e => e.Department)
                .Include(p => p.Employee)
                    .ThenInclude(e => e.Position)
                .Where(p => p.EmployeeId == employeeId)
                .OrderByDescending(p => p.Period.Year)
                .ThenByDescending(p => p.Period.Month)
                .ToListAsync();

            return _mapper.Map<List<PayslipDto>>(payslips);
        }

        public async Task<TaxCalculationResultDto> CalculateTaxAsync(TaxCalculationRequestDto request)
        {
            return await Task.FromResult(_taxService.Calculate(request.GrossIncome, request.NumberOfDependents, request.IsInsuranceApplicable));
        }

        // ── Helper methods ─────────────────────────────────────────────────────

        /// <summary>
        /// Mẫu số: Số ngày được phân ca làm việc (ShiftAssignment) trong kỳ.
        /// Fallback về đếm T2-T6 nếu nhân viên chưa được phân ca.
        /// </summary>
        private async Task<decimal> CalculateAssignedWorkingDaysAsync(int employeeId, PayrollPeriod period)
        {
            var assignedDays = await _context.ShiftAssignments
                .Where(sa => sa.EmployeeId == employeeId
                    && sa.AssignmentDate >= period.StartDate
                    && sa.AssignmentDate <= period.EndDate)
                .Select(sa => sa.AssignmentDate)
                .Distinct()
                .ToListAsync();

            // Paid public holidays are working days the employee is paid for, whether or not a shift was assigned
            var holidays = await PaidWeekdayHolidaysAsync(employeeId, period);
            return assignedDays.Union(holidays).Count();
        }

        /// <summary>
        /// Paid holidays on Monday–Friday within the period, from the employee's join date on.
        /// </summary>
        private async Task<HashSet<DateOnly>> PaidWeekdayHolidaysAsync(int employeeId, PayrollPeriod period)
        {
            var joinDate = await _context.Employees
                .Where(e => e.EmployeeId == employeeId)
                .Select(e => e.JoinDate)
                .FirstOrDefaultAsync();
            var from = joinDate > period.StartDate ? joinDate : period.StartDate;

            var dates = await _context.Holidays
                .Where(h => h.IsPaid && h.HolidayDate >= from && h.HolidayDate <= period.EndDate)
                .Select(h => h.HolidayDate)
                .ToListAsync();
            return dates.Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToHashSet();
        }

        /// <summary>
        /// Tính ngày T2–T6 trong khoảng — dùng làm fallback khi chưa có ShiftAssignment.
        /// </summary>
        private static decimal CalculateWeekdayCount(DateOnly startDate, DateOnly endDate)
        {
            var count = 0;
            var current = startDate;
            while (current <= endDate)
            {
                if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                    count++;
                current = current.AddDays(1);
            }
            return count;
        }

        /// <summary>
        /// Tử số: Ngày công thực tế quy đổi = Σ(WorkingHours / ShiftStandardHours).
        /// Tính cho:
        ///   - AttendanceRecord Status = "Present" hoặc "Late" (đi làm bình thường)
        ///   - AttendanceRecord có ExplanationStatus = "Approved" (giải trình được phê duyệt)
        /// Cộng thêm ngày nghỉ có lương (LeaveRequest Approved + IsPaid).
        /// </summary>
        private async Task<decimal> CalculateActualWorkingDaysAsync(int employeeId, PayrollPeriod period)
        {
            // Bước 1: Lấy ShiftAssignment (chỉ lấy scalar, tránh navigation property sau GroupBy)
            var assignments = await _context.ShiftAssignments
                .Where(sa => sa.EmployeeId == employeeId
                    && sa.AssignmentDate >= period.StartDate
                    && sa.AssignmentDate <= period.EndDate)
                .Select(sa => new { sa.AssignmentDate, sa.ShiftId })
                .ToListAsync();

            // Lấy thông tin giờ chuẩn của từng Shift
            var shiftIds = assignments.Select(a => a.ShiftId).Distinct().ToList();
            var shiftHoursMap = shiftIds.Count > 0
                ? await _context.Shifts
                    .Where(s => shiftIds.Contains(s.ShiftId))
                    .ToDictionaryAsync(s => s.ShiftId, s => (decimal)s.WorkingHours)
                : new Dictionary<int, decimal>();

            // Xây dựng lookup: date → stdHours
            var stdHoursByDate = assignments
                .GroupBy(a => a.AssignmentDate)
                .ToDictionary(
                    g => g.Key,
                    g => shiftHoursMap.TryGetValue(g.First().ShiftId, out var h) ? h : 8m
                );

            // Bước 2: Lấy AttendanceRecord hợp lệ (không Include navigation để tránh lỗi)
            var validRecords = await _context.AttendanceRecords
                .Where(a => a.EmployeeId == employeeId
                    && a.AttendanceDate >= period.StartDate
                    && a.AttendanceDate <= period.EndDate
                    && ((a.Status == "Present" || a.Status == "Late")
                        || a.ExplanationStatus == "Approved"))
                .Select(a => new
                {
                    a.AttendanceDate,
                    a.WorkingHours,
                    a.ShiftId,
                })
                .ToListAsync();

            // Lấy ShiftId từ AttendanceRecord (phòng trường hợp AttendanceRecord có ShiftId khác với Assignment)
            var attShiftIds = validRecords.Where(r => r.ShiftId.HasValue)
                                          .Select(r => r.ShiftId!.Value)
                                          .Distinct()
                                          .Except(shiftIds)
                                          .ToList();
            if (attShiftIds.Count > 0)
            {
                var extraShifts = await _context.Shifts
                    .Where(s => attShiftIds.Contains(s.ShiftId))
                    .ToDictionaryAsync(s => s.ShiftId, s => (decimal)s.WorkingHours);
                foreach (var kv in extraShifts) shiftHoursMap[kv.Key] = kv.Value;
            }

            // Bước 3: Quy đổi sang ngày công
            decimal actualDays = 0m;
            foreach (var rec in validRecords)
            {
                // Số giờ chuẩn: ưu tiên từ ShiftAssignment → ShiftId trên record → mặc định 8
                decimal stdHours = stdHoursByDate.TryGetValue(rec.AttendanceDate, out var sh1) ? sh1
                                 : (rec.ShiftId.HasValue && shiftHoursMap.TryGetValue(rec.ShiftId.Value, out var sh2)) ? sh2
                                 : 8m;

                // Số giờ thực làm
                decimal workedHours = rec.WorkingHours.HasValue && rec.WorkingHours.Value > 0
                    ? rec.WorkingHours.Value
                    : stdHours;

                // A day counts once at most: check-out minus check-in includes the lunch break and early arrival,
                // and time beyond the shift is paid through approved overtime requests
                actualDays += Math.Min(workedHours / stdHours, 1m);
            }

            // Bước 4: Cộng ngày nghỉ có lương
            actualDays += await CalculatePaidLeaveDaysAsync(employeeId, period);

            // Bước 5: Ngày lễ có lương mà nhân viên không đi làm được tính đủ một công
            // (đi làm ngày lễ thì công đã tính ở bước 3; phần làm thêm hưởng hệ số OT ngày lễ)
            var workedDates = validRecords.Select(r => r.AttendanceDate).ToHashSet();
            var holidays = await PaidWeekdayHolidaysAsync(employeeId, period);
            actualDays += holidays.Count(d => !workedDates.Contains(d));

            return actualDays;
        }

        /// <summary>
        /// Tính số ngày nghỉ có lương (LeaveRequest Approved + IsPaid) trong kỳ,
        /// bỏ qua cuối tuần.
        /// </summary>
        private async Task<decimal> CalculatePaidLeaveDaysAsync(int employeeId, PayrollPeriod period)
        {
            var requests = await _context.LeaveRequests
                .Include(l => l.LeaveType)
                .Where(l => l.EmployeeId == employeeId
                    && l.Status == "Approved"
                    && l.StartDate <= period.EndDate
                    && l.EndDate >= period.StartDate
                    && l.LeaveType.IsPaid)
                .ToListAsync();

            // A leave that spans a public holiday does not count the holiday again: it is already a paid day
            var holidays = await PaidWeekdayHolidaysAsync(employeeId, period);

            decimal paidLeaveDays = 0;
            foreach (var req in requests)
            {
                var start = req.StartDate > period.StartDate ? req.StartDate : period.StartDate;
                var end = req.EndDate < period.EndDate ? req.EndDate : period.EndDate;
                var current = start;
                while (current <= end)
                {
                    if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday
                        && !holidays.Contains(current))
                        paidLeaveDays++;
                    current = current.AddDays(1);
                }
            }
            return paidLeaveDays;
        }

        private async Task<List<PayrollAllowance>> BuildAllowancesAsync(Employee employee, PayrollPeriod period)
        {
            var policies = await _context.PayrollPolicies
                .Where(p => p.PolicyType == "Allowance"
                    && p.IsActive
                    && (p.ApplicableEmployeeGroup == "All" || p.ApplicableEmployeeGroup == employee.EmploymentType))
                .ToListAsync();

            return policies.Select(p => new PayrollAllowance
            {
                AllowanceType = "Policy",
                AllowanceName = p.PolicyName,
                Amount = p.BaseAmount, // Tạm thời dùng BaseAmount cố định
                Description = p.Description,
            }).ToList();
        }

        private async Task<(decimal OvertimePay, List<PayrollAllowance> Details)>
            CalculateOvertimeAsync(int employeeId, PayrollPeriod period, decimal baseSalary, decimal workingDays,
                HRManagement.DTOs.SystemSettings.PayrollCalculationSettingsDto cfg)
        {
            var overtimeRequests = await _context.OvertimeRequests
                .Where(o => o.EmployeeId == employeeId
                    && o.Status == "Approved"
                    && o.OvertimeDate >= period.StartDate
                    && o.OvertimeDate <= period.EndDate)
                .ToListAsync();

            if (!overtimeRequests.Any())
                return (0m, new List<PayrollAllowance>());

            // Lương giờ = BaseSalary / (số ngày phân ca × 8 giờ/ngày)
            // Dùng workingDays (số ca phân công) × 8 làm mẫu số chuẩn
            var hourlySalary = workingDays > 0 ? baseSalary / (workingDays * 8) : 0m;
            var details = new List<PayrollAllowance>();
            var totalOt = 0m;

            // Overtime on a public holiday is paid at the holiday rate, also when the holiday falls on a weekend
            var holidayDates = (await _context.Holidays
                    .Where(h => h.HolidayDate >= period.StartDate && h.HolidayDate <= period.EndDate)
                    .Select(h => h.HolidayDate)
                    .ToListAsync())
                .ToHashSet();

            foreach (var ot in overtimeRequests)
            {
                var rate = holidayDates.Contains(ot.OvertimeDate)
                    ? cfg.OtHolidayMultiplier
                    : ot.OvertimeDate.DayOfWeek switch
                    {
                        DayOfWeek.Saturday or DayOfWeek.Sunday => cfg.OtWeekendMultiplier,
                        _ => cfg.OtWeekdayMultiplier,
                    };

                var otPay = Math.Round(hourlySalary * (decimal)ot.TotalHours * rate, 0);
                totalOt += otPay;

                details.Add(new PayrollAllowance
                {
                    AllowanceType = "Overtime",
                    AllowanceName = $"Lương OT {ot.OvertimeDate:dd/MM} (x{rate})",
                    Amount = otPay,
                    Description = ot.Reason,
                });
            }

            return (totalOt, details);
        }

        // ── AttendanceReview ───────────────────────────────────────────────────

        public async Task<PayrollPeriodDto> TriggerAttendanceReviewAsync(int periodId)
        {
            var period = await _periodRepo.GetByIdAsync(periodId)
                ?? throw new KeyNotFoundException("Không tìm thấy kỳ lương.");

            if (period.Status != "Open")
                throw new InvalidOperationException("Chỉ có thể kích hoạt review khi kỳ lương đang ở trạng thái Open.");

            period.Status         = "AttendanceReview";
            period.ReviewDeadline = period.AttendanceCutoffDate.ToDateTime(TimeOnly.MinValue).AddDays(period.ReviewWindowDays);
            await _periodRepo.UpdateAsync(period);

            // Gửi email thông báo cho toàn bộ NV active trong kỳ
            await SendAttendanceReviewEmailsAsync(period);

            var dto = _mapper.Map<PayrollPeriodDto>(period);
            dto.ReviewDeadline        = period.ReviewDeadline;
            dto.ReviewDeadlineExpired = false;
            return dto;
        }

        private async Task SendAttendanceReviewEmailsAsync(PayrollPeriod period)
        {
            var employees = await _context.Employees
                .Where(e => e.EmploymentStatus == "Active" && e.JoinDate <= period.EndDate && e.Email != null)
                .Select(e => new { e.EmployeeId, e.FullName, e.Email })
                .ToListAsync();

            var deadline = period.ReviewDeadline?.ToString("dd/MM/yyyy") ?? "";
            var subject  = $"[Thông báo] Xem xét chấm công tháng {period.Month}/{period.Year}";

            foreach (var emp in employees)
            {
                try
                {
                    var body = $@"<p>Xin chào <strong>{emp.FullName}</strong>,</p>
<p>Kỳ lương tháng <strong>{period.Month}/{period.Year}</strong> ({period.StartDate:dd/MM/yyyy} – {period.EndDate:dd/MM/yyyy}) đã bước vào giai đoạn xem xét chấm công.</p>
<p>Vui lòng kiểm tra bảng chấm công đính kèm và đăng nhập hệ thống để xem chi tiết trước ngày <strong>{deadline}</strong>.</p>
<p>Nếu có sai sót, hãy gửi yêu cầu giải trình cho quản lý trực tiếp để được xem xét và điều chỉnh.</p>
<p>Sau thời hạn trên, bộ phận HR sẽ tiến hành tính lương dựa trên dữ liệu chấm công hiện tại.</p>
<br/><p>Trân trọng,<br/>Phòng Nhân sự</p>";

                    var excelBytes = await BuildAttendanceExcelAsync(emp.EmployeeId, emp.FullName ?? "", period);
                    var fileName   = $"ChamCong_{emp.EmployeeId}_T{period.Month}_{period.Year}.xlsx";

                    await _emailService.SendWithAttachmentAsync(
                        emp.Email!, subject, body,
                        excelBytes, fileName,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                }
                catch
                {
                    // Không để lỗi email/excel của 1 NV block các NV còn lại
                }
            }
        }

        private async Task<byte[]> BuildAttendanceExcelAsync(int employeeId, string fullName, PayrollPeriod period)
        {
            var attendanceRecords = await _context.AttendanceRecords
                .Where(a => a.EmployeeId == employeeId
                    && a.AttendanceDate >= period.StartDate
                    && a.AttendanceDate <= period.EndDate)
                .OrderBy(a => a.AttendanceDate)
                .ToListAsync();

            var leaveRequests = await _context.LeaveRequests
                .Include(l => l.LeaveType)
                .Where(l => l.EmployeeId == employeeId
                    && l.Status == "Approved"
                    && l.StartDate <= period.EndDate
                    && l.EndDate   >= period.StartDate)
                .ToListAsync();

            var overtimeRequests = await _context.OvertimeRequests
                .Where(o => o.EmployeeId == employeeId
                    && o.Status == "Approved"
                    && o.OvertimeDate >= period.StartDate
                    && o.OvertimeDate <= period.EndDate)
                .ToListAsync();

            // Build lookup maps
            var attMap = attendanceRecords.ToDictionary(a => a.AttendanceDate);
            var otMap  = overtimeRequests.ToDictionary(o => o.OvertimeDate, o => o.TotalHours);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Bảng Chấm Công");

            // ── Header info ────────────────────────────────────────────────────
            ws.Cell("A1").Value = $"BẢNG CHẤM CÔNG THÁNG {period.Month}/{period.Year}";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Range("A1:H1").Merge();

            ws.Cell("A2").Value = $"Nhân viên: {fullName}";
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Range("A2:H2").Merge();

            ws.Cell("A3").Value = $"Kỳ: {period.StartDate:dd/MM/yyyy} – {period.EndDate:dd/MM/yyyy}";
            ws.Range("A3:H3").Merge();

            // ── Column headers ─────────────────────────────────────────────────
            var headers = new[] { "Ngày", "Thứ", "Trạng thái", "Giờ làm", "Giải trình", "Loại nghỉ", "OT (giờ)", "Ghi chú" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(5, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#4472C4");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            // ── Data rows ──────────────────────────────────────────────────────
            var dayNames = new[] { "CN", "T2", "T3", "T4", "T5", "T6", "T7" };
            int row = 6;

            for (var date = period.StartDate; date <= period.EndDate; date = date.AddDays(1))
            {
                bool isWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;

                attMap.TryGetValue(date, out var att);
                otMap.TryGetValue(date, out var otHours);

                var leaveItem = leaveRequests.FirstOrDefault(l => l.StartDate <= date && l.EndDate >= date);

                string status    = isWeekend ? "Nghỉ cuối tuần"
                                 : leaveItem != null ? (leaveItem.LeaveType?.IsPaid == true ? "Nghỉ phép" : "Nghỉ không lương")
                                 : att?.Status ?? "—";
                string explanation = att?.ExplanationStatus == "Approved" ? "Đã duyệt" : "";
                string leaveType   = leaveItem?.LeaveType?.LeaveTypeName ?? "";
                decimal workHours  = (decimal)(att?.WorkingHours ?? 0);

                ws.Cell(row, 1).Value = date.ToDateTime(TimeOnly.MinValue).ToString("dd/MM/yyyy");
                ws.Cell(row, 2).Value = dayNames[(int)date.DayOfWeek];
                ws.Cell(row, 3).Value = status;
                if (workHours > 0) ws.Cell(row, 4).Value = workHours; else ws.Cell(row, 4).Value = "—";
                ws.Cell(row, 5).Value = explanation;
                ws.Cell(row, 6).Value = leaveType;
                if (otHours > 0) ws.Cell(row, 7).Value = otHours; else ws.Cell(row, 7).Value = "—";
                ws.Cell(row, 8).Value = "";

                // Highlight weekends and leave rows
                var rowRange = ws.Range(row, 1, row, 8);
                if (isWeekend)
                    rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
                else if (leaveItem != null)
                    rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2EFDA");
                else if (status == "Absent")
                    rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE0E0");
                else if (status == "Late")
                    rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");

                foreach (var c in rowRange.Cells())
                    c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                row++;
            }

            // ── Summary row ────────────────────────────────────────────────────
            row++;
            ws.Cell(row, 1).Value = "Tổng cộng";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 4).FormulaA1 = $"SUMIF(D6:D{row - 2},\"<>—\",D6:D{row - 2})";
            ws.Cell(row, 4).Style.Font.Bold = true;
            ws.Cell(row, 7).FormulaA1 = $"SUMIF(G6:G{row - 2},\"<>—\",G6:G{row - 2})";
            ws.Cell(row, 7).Style.Font.Bold = true;

            // ── Column widths ──────────────────────────────────────────────────
            ws.Column(1).Width = 14;
            ws.Column(2).Width = 6;
            ws.Column(3).Width = 20;
            ws.Column(4).Width = 10;
            ws.Column(5).Width = 14;
            ws.Column(6).Width = 18;
            ws.Column(7).Width = 12;
            ws.Column(8).Width = 20;

            ws.SheetView.FreezeRows(5);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public async Task<PayrollRecordDto> GetMyRecordInPeriodAsync(int userId, int periodId)
        {
            var employeeId = await _context.Users
                .Where(u => u.UserId == userId && u.IsActive)
                .Select(u => u.EmployeeId)
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("Tài khoản chưa liên kết nhân viên.");

            var period = await _context.PayrollPeriods.FindAsync(periodId)
                ?? throw new KeyNotFoundException("Không tìm thấy kỳ lương.");

            if (period.Status == "Open" || period.Status == "AttendanceReview" || period.Status == "Calculated")
                throw new InvalidOperationException("Phiếu lương chưa được tạo.");

            var record = await _context.PayrollRecords
                .Include(r => r.PayrollAllowances)
                .Include(r => r.PayrollDeductions)
                .Include(r => r.Employee).ThenInclude(e => e.Department)
                .Include(r => r.Employee).ThenInclude(e => e.Position)
                .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.PeriodId == periodId)
                ?? throw new KeyNotFoundException("Không tìm thấy bản ghi lương của bạn trong kỳ này.");

            var dto = _mapper.Map<PayrollRecordDto>(record);
            dto.PeriodStatus = period.Status;
            return dto;
        }

        public async Task<List<PayrollPeriodDto>> GetPeriodsForEmployeeAsync(int userId)
        {
            var employeeId = await _context.Users
                .Where(u => u.UserId == userId && u.IsActive)
                .Select(u => u.EmployeeId)
                .FirstOrDefaultAsync();

            if (employeeId == null) return new List<PayrollPeriodDto>();

            var periodIds = await _context.PayrollRecords
                .Where(r => r.EmployeeId == employeeId)
                .Select(r => r.PeriodId)
                .Distinct()
                .ToListAsync();

            var periods = await _context.PayrollPeriods
                .Where(p => periodIds.Contains(p.PeriodId))
                .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
                .ToListAsync();

            return _mapper.Map<List<PayrollPeriodDto>>(periods);
        }

        public async Task<AttendanceSummaryDto> GetMyAttendanceSummaryAsync(int userId, int periodId)
        {
            var employeeId = await _context.Users
                .Where(u => u.UserId == userId && u.IsActive)
                .Select(u => u.EmployeeId)
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("Tài khoản chưa liên kết nhân viên.");

            var period = await _context.PayrollPeriods.FindAsync(periodId)
                ?? throw new KeyNotFoundException("Không tìm thấy kỳ lương.");

            var startDt = period.StartDate.ToDateTime(TimeOnly.MinValue);
            var endDt   = period.EndDate.ToDateTime(TimeOnly.MaxValue);

            // Attendance records
            var attendanceRecords = await _context.AttendanceRecords
                .Where(a => a.EmployeeId == employeeId
                    && a.AttendanceDate >= period.StartDate
                    && a.AttendanceDate <= period.EndDate)
                .OrderBy(a => a.AttendanceDate)
                .ToListAsync();

            // Approved leave requests
            var leaveRequests = await _context.LeaveRequests
                .Include(l => l.LeaveType)
                .Where(l => l.EmployeeId == employeeId
                    && l.Status == "Approved"
                    && l.StartDate <= period.EndDate
                    && l.EndDate >= period.StartDate)
                .OrderBy(l => l.StartDate)
                .ToListAsync();

            // Approved overtime requests
            var overtimeRequests = await _context.OvertimeRequests
                .Where(o => o.EmployeeId == employeeId
                    && o.Status == "Approved"
                    && o.OvertimeDate >= period.StartDate
                    && o.OvertimeDate <= period.EndDate)
                .OrderBy(o => o.OvertimeDate)
                .ToListAsync();

            int presentDays = attendanceRecords.Count(a => a.Status == "Present");
            int lateDays    = attendanceRecords.Count(a => a.Status == "Late");
            int absentDays  = attendanceRecords.Count(a => a.Status == "Absent" && a.ExplanationStatus != "Approved");
            int explanationApprovedDays = attendanceRecords.Count(a => a.ExplanationStatus == "Approved");
            decimal overtimeHours = overtimeRequests.Sum(o => o.TotalHours);

            // Tính paid leave days (weekdays only, within period), like payroll: a public holiday is not a leave day
            var holidays = await PaidWeekdayHolidaysAsync(employeeId, period);
            decimal paidLeaveDays = 0;
            foreach (var leave in leaveRequests.Where(l => l.LeaveType?.IsPaid == true))
            {
                var s = leave.StartDate > period.StartDate ? leave.StartDate : period.StartDate;
                var e = leave.EndDate < period.EndDate ? leave.EndDate : period.EndDate;
                for (var d = s; d <= e; d = d.AddDays(1))
                {
                    if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday && !holidays.Contains(d))
                        paidLeaveDays++;
                }
            }

            var shiftCount = await _context.ShiftAssignments
                .Where(sa => sa.EmployeeId == employeeId
                    && sa.AssignmentDate >= period.StartDate
                    && sa.AssignmentDate <= period.EndDate)
                .CountAsync();

            var totalActualDays = (decimal)(presentDays + lateDays + explanationApprovedDays) + paidLeaveDays;

            return new AttendanceSummaryDto
            {
                Records = attendanceRecords.Select(a => new AttendanceItemDto
                {
                    Date                 = a.AttendanceDate.ToString("yyyy-MM-dd"),
                    Status               = a.Status ?? "",
                    WorkingHours         = (decimal)(a.WorkingHours ?? 0),
                    IsExplanationApproved = a.ExplanationStatus == "Approved",
                }).ToList(),
                ApprovedLeaves = leaveRequests.Select(l => new LeaveItemDto
                {
                    StartDate     = l.StartDate.ToString("yyyy-MM-dd"),
                    EndDate       = l.EndDate.ToString("yyyy-MM-dd"),
                    LeaveTypeName = l.LeaveType?.LeaveTypeName ?? "",
                    IsPaid        = l.LeaveType?.IsPaid ?? false,
                    Days          = (decimal)(l.EndDate.DayNumber - l.StartDate.DayNumber + 1),
                }).ToList(),
                ApprovedOvertime = overtimeRequests.Select(o => new OvertimeItemDto
                {
                    Date  = o.OvertimeDate.ToString("yyyy-MM-dd"),
                    Hours = o.TotalHours,
                }).ToList(),
                Totals = new AttendanceTotalsDto
                {
                    PresentDays              = presentDays,
                    LateDays                 = lateDays,
                    AbsentDays               = absentDays,
                    ExplanationApprovedDays  = explanationApprovedDays,
                    PaidLeaveDays            = paidLeaveDays,
                    OvertimeHours            = overtimeHours,
                    TotalActualDays          = totalActualDays,
                },
            };
        }
    }
}
