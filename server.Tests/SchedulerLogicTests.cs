using System;
using SigortaTakip.Services;

namespace SigortaTakip.Tests
{
    public class SchedulerLogicTests
    {
        [Fact]
        public void ParseReminderDays_DefaultsWhenEmptyOrInvalid()
        {
            Assert.Equal(new[] { 15, 7, 1, 0 }, SchedulerService.ParseReminderDays(null));
            Assert.Equal(new[] { 15, 7, 1, 0 }, SchedulerService.ParseReminderDays(""));
            Assert.Equal(new[] { 15, 7, 1, 0 }, SchedulerService.ParseReminderDays("abc,xyz"));
        }

        [Fact]
        public void ParseReminderDays_ParsesSortsDistinctsAndAlwaysIncludesZero()
        {
            Assert.Equal(new[] { 30, 15, 7, 1, 0 }, SchedulerService.ParseReminderDays("30,15,7,1,0"));
            // Zero is appended when missing; duplicates dropped; sorted descending.
            Assert.Equal(new[] { 5, 3, 0 }, SchedulerService.ParseReminderDays("5, 3, 5"));
            // Negative values are ignored.
            Assert.Equal(new[] { 10, 0 }, SchedulerService.ParseReminderDays("10,-4"));
        }

        [Fact]
        public void DaysRemaining_CountsCalendarDays()
        {
            var today = new DateTime(2026, 6, 7);
            Assert.Equal(3, SchedulerService.DaysRemaining("2026-06-10", today));
            Assert.Equal(0, SchedulerService.DaysRemaining("2026-06-07", today));
            Assert.Equal(-2, SchedulerService.DaysRemaining("2026-06-05", today));
        }

        [Fact]
        public void DaysRemaining_NullOnBadFormat()
        {
            Assert.Null(SchedulerService.DaysRemaining("not-a-date", new DateTime(2026, 6, 7)));
        }

        [Theory]
        [InlineData(20, null)]   // outside the warning window
        [InlineData(15, 15)]     // exactly the widest threshold
        [InlineData(10, 15)]     // still in the 15 bucket
        [InlineData(7, 7)]
        [InlineData(1, 1)]
        [InlineData(0, 0)]       // expiry day
        [InlineData(-5, 0)]      // already expired maps to the 0 bucket
        public void CurrentThreshold_PicksMostUrgentReachedBucket(int daysRemaining, int? expected)
        {
            var thresholds = new[] { 15, 7, 1, 0 };
            Assert.Equal(expected, SchedulerService.CurrentThreshold(daysRemaining, thresholds));
        }
    }
}
