namespace HRManagement.Tests.Configuration
{
    // AppTime is static; xUnit runs the tests of one class one after another, and Dispose restores the real clock
    public class AppTimeTests : IDisposable
    {
        private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => utcNow;
        }

        public void Dispose() => AppTime.Provider = TimeProvider.System;

        [Fact]
        public void Now_is_Vietnam_time_whatever_the_server_zone()
        {
            AppTime.Provider = new FixedTime(new DateTimeOffset(2026, 10, 12, 3, 0, 0, TimeSpan.Zero));

            Assert.Equal(new DateTime(2026, 10, 12, 10, 0, 0), AppTime.Now);
            Assert.Equal(DateTimeKind.Unspecified, AppTime.Now.Kind);
        }

        [Fact]
        public void Today_turns_over_at_midnight_in_Vietnam_not_in_UTC()
        {
            // 18:30 UTC on the 12th is 01:30 on the 13th in Vietnam
            AppTime.Provider = new FixedTime(new DateTimeOffset(2026, 10, 12, 18, 30, 0, TimeSpan.Zero));

            Assert.Equal(new DateTime(2026, 10, 13), AppTime.Today);
        }
    }
}
