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
                return StatusCode(500, new { error = "Failed to trigger check", details = ex.Message });
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

                var currentData = Db.ReadDb();

                var data = new DatabaseData
                {
                    Settings = req.Settings ?? currentData.Settings,
                    Buses = req.Buses,
                    Users = currentData.Users // Preserve users!
                };

                Db.WriteDb(data);
                return Ok(new { success = true, message = "Veritabanı başarıyla geri yüklendi" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Geri yükleme başarısız oldu", details = ex.Message });
            }
        }
    }
}
