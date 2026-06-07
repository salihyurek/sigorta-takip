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
            // Superadmin-only: the SMTP host/user/sender/receiver are infrastructure
            // details that shouldn't be exposed to viewer-role accounts.
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

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
                Console.WriteLine($"[Settings] GetSettings failed: {ex}");
                return StatusCode(500, new { error = "Ayarlar okunamadı." });
            }
        }

        [HttpPost]
        public IActionResult SaveSettings([FromBody] Settings newSettings)
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                // Resolve the effective password (a masked value means "keep existing")
                // so we can tell whether emails can actually be sent.
                var existingPass = Db.ReadDb().Settings.SmtpPass ?? "";
                var effectivePass = newSettings.SmtpPass == "********" ? existingPass : (newSettings.SmtpPass ?? "");

                var validationError = ValidateSmtpSettings(newSettings, effectivePass);
                if (validationError != null) return BadRequest(new { error = validationError });

                Settings? saved = null;

                var ok = Db.Update(data =>
                {
                    // If client sends masked password, keep the old password
                    string smtpPass = newSettings.SmtpPass ?? "";
                    if (smtpPass == "********")
                    {
                        smtpPass = data.Settings.SmtpPass ?? "";
                    }

                    saved = new Settings
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
                    data.Settings = saved;
                    return true;
                });

                if (!ok) return StatusCode(500, new { error = "Ayarlar kaydedilemedi." });

                // Mask password in response
                var responseSettings = new Settings
                {
                    SmtpHost = saved!.SmtpHost,
                    SmtpPort = saved.SmtpPort,
                    SmtpUser = saved.SmtpUser,
                    SenderName = saved.SenderName,
                    SenderEmail = saved.SenderEmail,
                    ReceiverEmail = saved.ReceiverEmail,
                    EnableEmails = saved.EnableEmails
                };

                if (!string.IsNullOrEmpty(saved.SmtpPass))
                {
                    responseSettings.SmtpPass = "********";
                }

                return Ok(new { success = true, settings = responseSettings });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Settings] SaveSettings failed: {ex}");
                return StatusCode(500, new { error = "Ayarlar kaydedilemedi." });
            }
        }

        /// <summary>
        /// Server-side guard so a bad config can't be persisted (the client's required=
        /// attributes are advisory only). Port must be a valid TCP port; when email is
        /// enabled the SMTP/sender/receiver fields must be present and well-formed.
        /// </summary>
        private static string? ValidateSmtpSettings(Settings s, string effectivePass)
        {
            if (s.SmtpPort < 1 || s.SmtpPort > 65535)
            {
                return "SMTP portu 1-65535 aralığında olmalıdır.";
            }

            if (!s.EnableEmails) return null; // Nothing else matters while disabled.

            if (string.IsNullOrWhiteSpace(s.SmtpHost)) return "E-posta etkinken SMTP sunucusu zorunludur.";
            if (string.IsNullOrWhiteSpace(s.SmtpUser)) return "E-posta etkinken SMTP kullanıcı adı zorunludur.";
            if (string.IsNullOrWhiteSpace(effectivePass)) return "E-posta etkinken SMTP şifresi zorunludur.";
            if (string.IsNullOrWhiteSpace(s.SenderEmail)) return "E-posta etkinken gönderen e-posta adresi zorunludur.";
            if (string.IsNullOrWhiteSpace(s.ReceiverEmail)) return "E-posta etkinken alıcı e-posta adresi zorunludur.";

            if (!IsValidEmail(s.SenderEmail)) return "Gönderen e-posta adresi geçersiz.";
            if (!IsValidEmail(s.ReceiverEmail)) return "Alıcı e-posta adresi geçersiz.";

            return null;
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
                return Ok(new { success = true, message = "Test e-postası başarıyla gönderildi!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Settings] Test email failed: {ex}");
                // The SMTP error text is genuinely useful for the admin configuring email,
                // so surface a trimmed version here (this endpoint is superadmin-only).
                return StatusCode(500, new { error = "Test e-postası gönderilemedi.", details = ex.Message });
            }
        }
    }
}
