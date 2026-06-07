using System;

namespace SigortaTakip.Services
{
    /// <summary>
    /// Centralizes "what day is it" logic so the whole app agrees on a single
    /// business timezone regardless of where the server (or container) runs.
    /// Containers default to UTC; without this, "expires today" emails and the
    /// 08:00 daily check would fire on the wrong day for a Turkey-based agency.
    /// Configure with the APP_TIMEZONE env var (IANA id, e.g. "Europe/Istanbul").
    /// </summary>
    public class TimeService
    {
        private readonly TimeZoneInfo _tz;

        public TimeService()
        {
            var tzId = Environment.GetEnvironmentVariable("APP_TIMEZONE");
            _tz = ResolveTimeZone(tzId);
            Console.WriteLine($"[Time] Application timezone: {_tz.Id}");
        }

        private static TimeZoneInfo ResolveTimeZone(string? configured)
        {
            // Try the configured id first, then the common Windows/IANA names for Turkey,
            // finally fall back to UTC so the app never crashes on an unknown platform id.
            var candidates = new[]
            {
                configured,
                "Europe/Istanbul",      // IANA (Linux/macOS)
                "Turkey Standard Time"  // Windows
            };

            foreach (var id in candidates)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch { /* try next */ }
            }

            Console.WriteLine("[Time] WARNING: could not resolve Europe/Istanbul timezone, falling back to UTC.");
            return TimeZoneInfo.Utc;
        }

        /// <summary>Current wall-clock time in the configured business timezone.</summary>
        public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);

        /// <summary>Today's date in the business timezone, formatted yyyy-MM-dd.</summary>
        public string TodayString => Now.ToString("yyyy-MM-dd");

        /// <summary>Date N days from today (business timezone), formatted yyyy-MM-dd.</summary>
        public string DateString(int offsetDays) => Now.Date.AddDays(offsetDays).ToString("yyyy-MM-dd");
    }
}
