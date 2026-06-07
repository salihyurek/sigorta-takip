using System;
using System.Collections.Generic;
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
        private int _lastRunHour = -1;
        private DateTime _lastRunDate = DateTime.MinValue;

        public SchedulerService(DbService dbService, MailService mailService)
        {
            _dbService = dbService;
            _mailService = mailService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("[Scheduler] Cron scheduler initialized.");

            // 1. Run check on startup after a small delay (10 seconds)
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

            // 2. Loop to check periodic times (runs every 5 minutes to check target hours)
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    int currentHour = now.Hour;
                    var currentDate = now.Date;

                    // Check if we already ran in this hour to prevent double runs
                    if (_lastRunHour != currentHour || _lastRunDate != currentDate)
                    {
                        bool shouldRun = false;

                        // Daily scheduled check (08:00 AM)
                        if (currentHour == 8)
                        {
                            Console.WriteLine("[Scheduler] Running daily scheduled check (08:00 AM)...");
                            shouldRun = true;
                        }
                        // Periodic check (every 4 hours: 00:00, 04:00, 12:00, 16:00, 20:00)
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

                    // Sleep for 5 minutes before checking the clock again
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Scheduler] Periodic check loop failed: {ex.Message}");
                    // Wait a bit before retrying on general errors
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }

        public async Task<int> CheckAllExpiriesAsync()
        {
            try
            {
                Console.WriteLine($"[Scheduler] Checking for expiring policies. Current time: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
                
                var data = _dbService.ReadDb();
                var settings = data.Settings;

                if (settings == null || !settings.EnableEmails)
                {
                    Console.WriteLine("[Scheduler] Email notifications are disabled in settings. Skipping check.");
                    return 0;
                }

                var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
                Console.WriteLine($"[Scheduler] Checking for policies expiring today: {todayStr}");

                int emailsSent = 0;
                bool hasChanges = false;

                foreach (var bus in data.Buses)
                {
                    if (bus.Policies == null) continue;

                    foreach (var policyKey in new[] { "trafik", "kasko", "koltuk" })
                    {
                        if (bus.Policies.TryGetValue(policyKey, out var policy))
                        {
                            if (policy != null && policy.EndDate == todayStr)
                            {
                                // Check if we already sent an email for this policy today
                                if (policy.LastEmailedDate != todayStr)
                                {
                                    Console.WriteLine($"[Scheduler] Policy {policyKey} for bus {bus.Plate} expires today! Sending email...");
                                    try
                                    {
                                        await _mailService.SendExpiryAlertAsync(bus, policyKey, todayStr, settings);
                                        policy.LastEmailedDate = todayStr;
                                        emailsSent++;
                                        hasChanges = true;
                                        Console.WriteLine($"[Scheduler] Email sent successfully for {bus.Plate} - {policyKey}");
                                    }
                                    catch (Exception err)
                                    {
                                        Console.WriteLine($"[Scheduler] Failed to send email for {bus.Plate} - {policyKey}: {err.Message}");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"[Scheduler] Policy {policyKey} for bus {bus.Plate} expires today, but an email was already sent today.");
                                }
                            }
                        }
                    }
                }

                if (hasChanges)
                {
                    _dbService.WriteDb(data);
                    Console.WriteLine("[Scheduler] Database updated with email timestamps.");
                }

                Console.WriteLine($"[Scheduler] Expiry check complete. Sent {emailsSent} email alerts.");
                return emailsSent;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Scheduler] Exception in CheckAllExpiriesAsync: {ex.Message}");
                return 0;
            }
        }
    }
}
