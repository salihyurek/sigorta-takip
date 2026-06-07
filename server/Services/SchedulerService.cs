using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SigortaTakip.Models;

namespace SigortaTakip.Services
{
    public class SchedulerService : BackgroundService
    {
        private readonly DbService _dbService;
        private readonly MailService _mailService;
        private readonly TimeService _time;
        private readonly int[] _reminderDays;
        private int _lastRunHour = -1;
        private DateTime _lastRunDate = DateTime.MinValue;

        // Serializes expiry checks so the scheduled run and a manual "check now" trigger
        // can't run concurrently — that would let both read the same pre-notification
        // state and send duplicate emails / clobber each other's threshold writes.
        private readonly SemaphoreSlim _runLock = new(1, 1);

        public SchedulerService(DbService dbService, MailService mailService, TimeService time)
        {
            _dbService = dbService;
            _mailService = mailService;
            _time = time;
            _reminderDays = ParseReminderDays(Environment.GetEnvironmentVariable("REMINDER_DAYS"));
            Console.WriteLine($"[Scheduler] Reminder thresholds (days before expiry): {string.Join(", ", _reminderDays)}");
        }

        /// <summary>The configured reminder thresholds (days before expiry), e.g. 15,7,1,0.</summary>
        public int[] ReminderDays => _reminderDays;

        /// <summary>The widest threshold = the day the first reminder goes out. The UI uses
        /// this for its "expiring soon" window so the colour matches when emails start.</summary>
        public int SoonThresholdDays => _reminderDays.Length > 0 ? _reminderDays.Max() : 15;

        // Thresholds at which we send a warning, e.g. 15/7/1 days before and on expiry (0).
        // 0 is always included so the expiry day itself is never missed.
        internal static int[] ParseReminderDays(string? raw)
        {
            var defaults = new[] { 15, 7, 1, 0 };
            if (string.IsNullOrWhiteSpace(raw)) return defaults;

            var parsed = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var n) ? (int?)n : null)
                .Where(n => n.HasValue && n.Value >= 0)
                .Select(n => n!.Value)
                .ToList();

            if (parsed.Count == 0) return defaults;
            if (!parsed.Contains(0)) parsed.Add(0);
            return parsed.Distinct().OrderByDescending(n => n).ToArray();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("[Scheduler] Cron scheduler initialized.");

            try
            {
                await Task.Delay(10000, stoppingToken);
                Console.WriteLine("[Scheduler] Running initial startup check...");
                await CheckAllExpiriesAsync();
            }
            catch (TaskCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Scheduler] Startup check failed: {ex.Message}");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = _time.Now; // business timezone, not server/UTC time
                    int currentHour = now.Hour;
                    var currentDate = now.Date;

                    if (_lastRunHour != currentHour || _lastRunDate != currentDate)
                    {
                        bool shouldRun = false;

                        if (currentHour == 8)
                        {
                            Console.WriteLine("[Scheduler] Running daily scheduled check (08:00)...");
                            shouldRun = true;
                        }
                        else if (currentHour == 0 || currentHour == 4 || currentHour == 12 || currentHour == 16 || currentHour == 20)
                        {
                            Console.WriteLine($"[Scheduler] Running periodic check ({currentHour}:00)...");
                            shouldRun = true;
                        }

                        if (shouldRun)
                        {
                            _lastRunHour = currentHour;
                            _lastRunDate = currentDate;
                            await CheckAllExpiriesAsync();
                        }
                    }

                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Scheduler] Periodic check loop failed: {ex.Message}");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }

        internal static int? DaysRemaining(string endDate, DateTime today)
        {
            if (!DateTime.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var end))
            {
                return null;
            }
            return (end.Date - today.Date).Days;
        }

        // Most urgent reached threshold for a given days-remaining value, or null if none.
        // Smaller threshold = more urgent. Expired policies (negative days) map to 0.
        private int? CurrentThreshold(int daysRemaining) => CurrentThreshold(daysRemaining, _reminderDays);

        internal static int? CurrentThreshold(int daysRemaining, int[] reminderDays)
        {
            int? bucket = null;
            foreach (var t in reminderDays)
            {
                if (daysRemaining <= t)
                {
                    bucket = (bucket == null) ? t : Math.Min(bucket.Value, t);
                }
            }
            return bucket;
        }

        public async Task<int> CheckAllExpiriesAsync()
        {
            // Bounded wait so a slow/stuck run can't make callers (the scheduled tick or a
            // manual trigger) queue up indefinitely. If another check already holds the lock,
            // skip — it does the work, and the threshold dedup makes a concurrent run a no-op.
            if (!await _runLock.WaitAsync(TimeSpan.FromSeconds(30)))
            {
                Console.WriteLine("[Scheduler] Another expiry check is already running; skipping this trigger.");
                return 0;
            }
            try
            {
                var now = _time.Now;
                Console.WriteLine($"[Scheduler] Checking policies. Local time: {now:yyyy-MM-ddTHH:mm:ss}");

                var data = _dbService.ReadDb();
                var settings = data.Settings;

                if (settings == null || !settings.EnableEmails)
                {
                    Console.WriteLine("[Scheduler] Email notifications disabled. Skipping.");
                    return 0;
                }

                // Pass 1: figure out which policies need an email (pure, no I/O, no mutation).
                var pending = new List<(Bus bus, string key, string endDate, int days, int bucket)>();
                foreach (var bus in data.Buses)
                {
                    if (bus.Policies == null) continue;

                    foreach (var policyKey in new[] { "trafik", "kasko", "koltuk" })
                    {
                        if (!bus.Policies.TryGetValue(policyKey, out var policy) || policy == null) continue;

                        var days = DaysRemaining(policy.EndDate, now);
                        if (days == null) continue;

                        var bucket = CurrentThreshold(days.Value);
                        if (bucket == null) continue; // still outside the warning window

                        // Only notify when we've entered a more urgent bucket than last time.
                        // This resends nothing within the same bucket, yet still fires if the
                        // server was down on the exact threshold day (it catches up).
                        if (policy.LastNotifiedThreshold != null && policy.LastNotifiedThreshold.Value <= bucket.Value)
                        {
                            continue;
                        }

                        pending.Add((bus, policyKey, policy.EndDate, days.Value, bucket.Value));
                    }
                }

                if (pending.Count == 0)
                {
                    Console.WriteLine("[Scheduler] Check complete. Sent 0 email(s).");
                    return 0;
                }

                var todayStr = _time.TodayString;
                int emailsSent = 0;
                var sent = new List<(string busId, string key, string endDate, int bucket)>();

                // Pass 2: send over a single reused SMTP connection.
                using (var client = _mailService.CreateSmtpClient(settings))
                {
                    foreach (var item in pending)
                    {
                        try
                        {
                            await _mailService.SendPolicyReminderAsync(client, item.bus, item.key, item.endDate, item.days, settings);
                            sent.Add((item.bus.Id, item.key, item.endDate, item.bucket));
                            emailsSent++;
                            Console.WriteLine($"[Scheduler] Sent reminder for {item.bus.Plate} - {item.key} ({item.days} gün).");
                        }
                        catch (Exception err)
                        {
                            Console.WriteLine($"[Scheduler] Failed to email {item.bus.Plate} - {item.key}: {err.Message}");
                        }
                    }
                }

                // Pass 3: persist notification timestamps against FRESH data (re-read inside
                // Update) so we don't clobber any bus/settings change an admin made while the
                // emails were being sent. Only stamp a policy whose end date is still the one
                // we notified for — if it was renewed meanwhile, its notifications were reset.
                if (sent.Count > 0)
                {
                    var persisted = _dbService.Update(fresh =>
                    {
                        foreach (var s in sent)
                        {
                            var b = fresh.Buses.FirstOrDefault(x => x.Id == s.busId);
                            if (b?.Policies != null
                                && b.Policies.TryGetValue(s.key, out var p) && p != null
                                && p.EndDate == s.endDate)
                            {
                                p.LastNotifiedThreshold = s.bucket;
                                p.LastEmailedDate = todayStr;
                            }
                        }
                        return true;
                    });
                    Console.WriteLine(persisted
                        ? "[Scheduler] Database updated with notification timestamps."
                        : "[Scheduler] WARNING: could not persist notification timestamps; reminders may resend next run.");
                }

                Console.WriteLine($"[Scheduler] Check complete. Sent {emailsSent} email(s).");
                return emailsSent;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Scheduler] Exception in CheckAllExpiriesAsync: {ex.Message}");
                return 0;
            }
            finally
            {
                _runLock.Release();
            }
        }
    }
}
