using HRManagement.DTOs;
using HRManagement.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Services.Holidays
{
    public interface IHolidayService
    {
        Task<List<HolidayDto>> GetByYearAsync(int year);
        Task<HolidayDto> CreateAsync(HolidayWriteDto dto, int? createdBy);
        Task<HolidayDto> UpdateAsync(int id, HolidayWriteDto dto);
        Task DeleteAsync(int id);

        /// <summary>
        /// Adds Vietnam's fixed-date public holidays for <paramref name="year"/> that are not on file yet and returns
        /// how many were added. Tết, Hùng Kings' day and the extra National Day are announced each year and are
        /// entered by hand.
        /// </summary>
        Task<int> AddFixedVietnamHolidaysAsync(int year, int? createdBy);

        /// <summary>Holidays between the two dates, by date.</summary>
        Task<Dictionary<DateOnly, HolidayDto>> GetMapAsync(DateOnly from, DateOnly to);
    }

    public class HolidayService : IHolidayService
    {
        // Solar-calendar days off under the Labour Code, art. 112
        private static readonly (int Month, int Day, string Name)[] FixedVietnamHolidays =
        {
            (1, 1, "Tết Dương lịch"),
            (4, 30, "Ngày Chiến thắng"),
            (5, 1, "Ngày Quốc tế Lao động"),
            (9, 2, "Quốc khánh"),
        };

        private readonly HrmsDbContext _context;

        public HolidayService(HrmsDbContext context)
        {
            _context = context;
        }

        public async Task<List<HolidayDto>> GetByYearAsync(int year)
        {
            var from = new DateOnly(year, 1, 1);
            var to = new DateOnly(year, 12, 31);
            return await _context.Holidays.AsNoTracking()
                .Where(h => h.HolidayDate >= from && h.HolidayDate <= to)
                .OrderBy(h => h.HolidayDate)
                .Select(h => ToDto(h))
                .ToListAsync();
        }

        public async Task<HolidayDto> CreateAsync(HolidayWriteDto dto, int? createdBy)
        {
            var name = Validate(dto);
            if (await _context.Holidays.AnyAsync(h => h.HolidayDate == dto.HolidayDate))
                throw new InvalidOperationException($"Ngày {dto.HolidayDate:dd/MM/yyyy} đã có trong lịch nghỉ.");

            var holiday = new Holiday
            {
                HolidayDate = dto.HolidayDate,
                Name = name,
                IsPaid = dto.IsPaid,
                CreatedDate = AppTime.Now,
                CreatedBy = createdBy
            };
            _context.Holidays.Add(holiday);
            await _context.SaveChangesAsync();
            return ToDto(holiday);
        }

        public async Task<HolidayDto> UpdateAsync(int id, HolidayWriteDto dto)
        {
            var holiday = await _context.Holidays.FirstOrDefaultAsync(h => h.HolidayId == id)
                ?? throw new KeyNotFoundException("Không tìm thấy ngày nghỉ.");
            var name = Validate(dto);
            if (await _context.Holidays.AnyAsync(h => h.HolidayDate == dto.HolidayDate && h.HolidayId != id))
                throw new InvalidOperationException($"Ngày {dto.HolidayDate:dd/MM/yyyy} đã có trong lịch nghỉ.");

            holiday.HolidayDate = dto.HolidayDate;
            holiday.Name = name;
            holiday.IsPaid = dto.IsPaid;
            await _context.SaveChangesAsync();
            return ToDto(holiday);
        }

        public async Task DeleteAsync(int id)
        {
            var holiday = await _context.Holidays.FirstOrDefaultAsync(h => h.HolidayId == id)
                ?? throw new KeyNotFoundException("Không tìm thấy ngày nghỉ.");
            _context.Holidays.Remove(holiday);
            await _context.SaveChangesAsync();
        }

        public async Task<int> AddFixedVietnamHolidaysAsync(int year, int? createdBy)
        {
            if (year < 2000 || year > 2100)
                throw new ArgumentException("Năm không hợp lệ.");

            var existing = (await GetMapAsync(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31))).Keys.ToHashSet();
            var added = 0;
            foreach (var (month, day, name) in FixedVietnamHolidays)
            {
                var date = new DateOnly(year, month, day);
                if (existing.Contains(date))
                    continue;
                _context.Holidays.Add(new Holiday { HolidayDate = date, Name = name, IsPaid = true, CreatedDate = AppTime.Now, CreatedBy = createdBy });
                added++;
            }
            await _context.SaveChangesAsync();
            return added;
        }

        public async Task<Dictionary<DateOnly, HolidayDto>> GetMapAsync(DateOnly from, DateOnly to) =>
            await _context.Holidays.AsNoTracking()
                .Where(h => h.HolidayDate >= from && h.HolidayDate <= to)
                .Select(h => ToDto(h))
                .ToDictionaryAsync(h => h.HolidayDate);

        private static string Validate(HolidayWriteDto dto)
        {
            var name = dto.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Tên ngày nghỉ là bắt buộc.");
            return name;
        }

        private static HolidayDto ToDto(Holiday h) => new()
        {
            HolidayId = h.HolidayId,
            HolidayDate = h.HolidayDate,
            Name = h.Name,
            IsPaid = h.IsPaid
        };
    }
}
