using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SigortaTakip.Models;
using SigortaTakip.Services;

namespace SigortaTakip.Controllers
{
    [ApiController]
    [Route("api/settings")]
    public class SettingsController : BaseApiController
    {
        private readonly MailService _mailService;

        public SettingsController(DbService dbService, AuthService authService, MailService mailService)
            : base(dbService, authService)
        {
            _mailService = mailService;
        }

        [HttpGet]
        public IActionResult GetSettings()
        {
            var authCheck = CheckAuth();
            if (authCheck != null) return authCheck;

            try
            {
                var data = Db.ReadDb();
                var settings = new Settings
                {
                    SmtpHost = data.Settings.SmtpHost,
                    SmtpPort = data.Settings.SmtpPort,
                    SmtpUser = data.Settings.SmtpUser,
                    SenderName = data.Settings.SenderName,
                    SenderEmail = data.Settings.SenderEmail,
                    ReceiverEmail = data.Settings.ReceiverEmail,
                    EnableEmails = data.Settings.EnableEmails
                };

                // Mask password before sending
                if (!string.IsNullOrEmpty(data.Settings.SmtpPass))
                {
                    settings.SmtpPass = "********";
                }

                return Ok(settings);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to read settings", details = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult SaveSettings([FromBody] Settings newSettings)
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                var data = Db.ReadDb();

                // If client sends masked password, keep the old password
                string smtpPass = newSettings.SmtpPass;
                if (smtpPass == "********")
                {
                    smtpPass = data.Settings.SmtpPass ?? "";
                }

                data.Settings = new Settings
                {
                    SmtpHost = (newSettings.SmtpHost ?? "").Trim(),
                    SmtpPort = newSettings.SmtpPort,
                    SmtpUser = (newSettings.SmtpUser ?? "").Trim(),
                    SmtpPass = smtpPass,
                    SenderName = (newSettings.SenderName ?? "").Trim(),
                    SenderEmail = (newSettings.SenderEmail ?? "").Trim(),
                    ReceiverEmail = (newSettings.ReceiverEmail ?? "").Trim(),
                    EnableEmails = newSettings.EnableEmails
                };

                Db.WriteDb(data);

                // Mask password in response
                var responseSettings = new Settings
                {
                    SmtpHost = data.Settings.SmtpHost,
                    SmtpPort = data.Settings.SmtpPort,
                    SmtpUser = data.Settings.SmtpUser,
                    SenderName = data.Settings.SenderName,
                    SenderEmail = data.Settings.SenderEmail,
                    ReceiverEmail = data.Settings.ReceiverEmail,
                    EnableEmails = data.Settings.EnableEmails
                };

                if (!string.IsNullOrEmpty(data.Settings.SmtpPass))
                {
                    responseSettings.SmtpPass = "********";
                }

                return Ok(new { success = true, settings = responseSettings });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to save settings", details = ex.Message });
            }
        }

        [HttpPost("test-email")]
        public async Task<IActionResult> TestEmail([FromBody] Settings testSettings)
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                var data = Db.ReadDb();

                // If client sends masked password, load existing password
                if (testSettings.SmtpPass == "********")
                {
                    testSettings.SmtpPass = data.Settings.SmtpPass ?? "";
                }

                await _mailService.SendTestEmailAsync(testSettings);
                return Ok(new { success = true, message = "Test email sent successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test email sending error: {ex}");
                return StatusCode(500, new { error = "Failed to send test email", details = ex.Message });
            }
        }
    }
}
