namespace HRManagement
{
    /// <summary>
    /// The wall-clock time the business runs on (attendance, shifts, leave, payroll), independent of the server's
    /// own time zone: DateTime.Now would shift every check-in by 7 hours on a server set to UTC, and
    /// DateOnly.FromDateTime(DateTime.UtcNow) is still yesterday between midnight and 7am in Vietnam.
    /// Technical timestamps (token and OTP expiry) stay on DateTime.UtcNow.
    /// Lives in the root namespace so every HRManagement.* file sees it without a using.
    /// </summary>
    public static class AppTime
    {
        public const string DefaultTimeZoneId = "Asia/Ho_Chi_Minh";

        public static TimeZoneInfo TimeZone { get; private set; } = FindTimeZone(DefaultTimeZoneId);

        /// <summary>Swappable so tests can pin the clock.</summary>
        public static TimeProvider Provider { get; set; } = TimeProvider.System;

        /// <summary>Current local time in <see cref="TimeZone"/>; Kind is Unspecified, like the values stored in the database.</summary>
        public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(Provider.GetUtcNow().UtcDateTime, TimeZone);

        public static DateTime Today => Now.Date;

        public static void Configure(string? timeZoneId)
        {
            if (!string.IsNullOrWhiteSpace(timeZoneId))
                TimeZone = FindTimeZone(timeZoneId);
        }

        private static TimeZoneInfo FindTimeZone(string id)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) when (id == DefaultTimeZoneId)
            {
                // Windows without ICU only knows its own zone names
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
        }
    }
}
