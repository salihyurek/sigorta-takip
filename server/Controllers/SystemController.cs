using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SigortaTakip.Models;
using SigortaTakip.Services;

namespace SigortaTakip.Controllers
{
    [ApiController]
    [Route("api")]
    public class SystemController : BaseApiController
    {
        private readonly SchedulerService _schedulerService;

        public SystemController(DbService dbService, AuthService authService, SchedulerService schedulerService)
            : base(dbService, authService)
        {
            _schedulerService = schedulerService;
        }

        [HttpPost("check-expiries")]
        public async Task<IActionResult> CheckExpiries()
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                var sentCount = await _schedulerService.CheckAllExpiriesAsync();
                return Ok(new { success = true, sentCount });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[System] CheckExpiries failed: {ex}");
                return StatusCode(500, new { error = "Kontrol tetiklenemedi." });
            }
        }

        public class RestoreRequest
        {
            public Settings? Settings { get; set; }
            public List<Bus>? Buses { get; set; }
        }

        [HttpPost("restore")]
        public IActionResult Restore([FromBody] RestoreRequest req)
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                if (req.Buses == null)
                {
                    return BadRequest(new { error = "Geçersiz yedek dosyası" });
                }

                // Validate every restored bus so a malformed backup can't corrupt the db.
                for (int i = 0; i < req.Buses.Count; i++)
                {
                    var b = req.Buses[i];
                    var error = ValidateBus(b.Plate, b.Brand, b.Operator, b.Policies);
                    if (error != null)
                    {
                        return BadRequest(new { error = $"Yedekteki {i + 1}. araç geçersiz: {error}" });
                    }
                }

                Db.Update(data =>
                {
                    var restoredSettings = req.Settings ?? data.Settings;
                    // Backups never contain the real SMTP password (it's masked on export),
                    // so don't let a masked/empty value overwrite the working password.
                    if (restoredSettings.SmtpPass == "********" || string.IsNullOrEmpty(restoredSettings.SmtpPass))
                    {
                        restoredSettings.SmtpPass = data.Settings.SmtpPass;
                    }

                    data.Settings = restoredSettings;
                    data.Buses = req.Buses;
                    // data.Users is left untouched to preserve existing accounts.
                    return true;
                });

                return Ok(new { success = true, message = "Veritabanı başarıyla geri yüklendi" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[System] Restore failed: {ex}");
                return StatusCode(500, new { error = "Geri yükleme başarısız oldu." });
            }
        }

        [HttpGet("/api/health")]
        [Microsoft.AspNetCore.RateLimiting.DisableRateLimiting]
        public IActionResult Health()
        {
            return Ok(new { status = "ok", timestamp = DateTime.UtcNow });
        }
    }
}
