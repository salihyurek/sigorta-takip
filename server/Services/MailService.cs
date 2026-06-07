using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using SigortaTakip.Models;

namespace SigortaTakip.Services
{
    public class MailService
    {
        // Internal so the scheduler can create one client and reuse it across a batch of
        // reminder emails instead of opening a fresh connection per policy.
        internal SmtpClient CreateSmtpClient(Settings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.SmtpHost) ||
                string.IsNullOrWhiteSpace(settings.SmtpUser) ||
                string.IsNullOrWhiteSpace(settings.SmtpPass))
            {
                throw new InvalidOperationException("SMTP ayarları eksik. Lütfen SMTP ayarlarını panelden yapılandırın.");
            }

            // NOTE: System.Net.Mail.SmtpClient only supports STARTTLS (explicit TLS),
            // not implicit TLS on port 465. Use port 587 (STARTTLS) with providers like
            // Gmail/Outlook. Port 25 is plain. EnableSsl upgrades the connection via STARTTLS.
            if (settings.SmtpPort == 465)
            {
                throw new InvalidOperationException(
                    "Port 465 (örtük SSL) bu sürümde desteklenmiyor. Lütfen STARTTLS için 587 portunu kullanın.");
            }

            var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                Credentials = new NetworkCredential(settings.SmtpUser, settings.SmtpPass),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                EnableSsl = settings.SmtpPort != 25,
                Timeout = 20000
            };

            return client;
        }

        private string EscapeHtml(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return WebUtility.HtmlEncode(str);
        }

        private string GenerateEmailTemplate(string title, string message, Bus? busDetails = null, string? policyType = null, string? expiryDate = null, string? remainingText = null)
        {
            var policyNames = new Dictionary<string, string>
            {
                { "trafik", "Zorunlu Trafik Sigortası" },
                { "kasko", "Kasko" },
                { "koltuk", "Koltuk Sigortası" }
            };

            string headerClass = string.IsNullOrEmpty(policyType) ? "test" : "";
            string alertClass = string.IsNullOrEmpty(policyType) ? "test" : "";

            var busDetailsHtml = "";
            if (busDetails != null)
            {
                var policyRowHtml = "";
                if (!string.IsNullOrEmpty(policyType) && policyNames.TryGetValue(policyType, out var pName))
                {
                    var remainingRow = string.IsNullOrEmpty(remainingText) ? "" : $@"
                        <tr>
                            <th>Kalan Süre</th>
                            <td><strong>{EscapeHtml(remainingText)}</strong></td>
                        </tr>";

                    policyRowHtml = $@"
                        <tr>
                            <th>İlgili Sigorta</th>
                            <td><span class=""policy-badge"">{pName}</span></td>
                        </tr>
                        <tr>
                            <th>Bitiş Tarihi</th>
                            <td><strong>{expiryDate}</strong></td>
                        </tr>{remainingRow}";
                }

                busDetailsHtml = $@"
                    <table class=""details-table"">
                        <tr>
                            <th>Plaka</th>
                            <td>{EscapeHtml(busDetails.Plate)}</td>
                        </tr>
                        <tr>
                            <th>Marka / Model</th>
                            <td>{EscapeHtml(busDetails.Brand)}</td>
                        </tr>
                        <tr>
                            <th>Acente / Firma</th>
                            <td>{EscapeHtml(busDetails.Operator)}</td>
                        </tr>
                        {policyRowHtml}
                    </table>";
            }

            return $@"
    <!DOCTYPE html>
    <html>
      <head>
        <meta charset=""utf-8"">
        <style>
          body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background-color: #0f172a;
            color: #e2e8f0;
            margin: 0;
            padding: 20px;
          }}
          .container {{
            max-width: 600px;
            margin: 0 auto;
            background-color: #1e293b;
            border-radius: 16px;
            overflow: hidden;
            box-shadow: 0 10px 25px rgba(0,0,0,0.3);
            border: 1px solid #334155;
          }}
          .header {{
            background: linear-gradient(135deg, #ef4444 0%, #b91c1c 100%);
            padding: 30px;
            text-align: center;
          }}
          .header.test {{
            background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%);
          }}
          .header h1 {{
            margin: 0;
            font-size: 24px;
            font-weight: 700;
            color: #ffffff;
            letter-spacing: 0;
          }}
          .content {{
            padding: 30px;
          }}
          .alert-box {{
            background-color: rgba(239, 68, 68, 0.1);
            border-left: 4px solid #ef4444;
            padding: 15px;
            border-radius: 4px;
            margin-bottom: 25px;
            color: #fca5a5;
          }}
          .alert-box.test {{
            background-color: rgba(59, 130, 246, 0.1);
            border-left: 4px solid #3b82f6;
            color: #93c5fd;
          }}
          .details-table {{
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 25px;
          }}
          .details-table th, .details-table td {{
            padding: 12px;
            text-align: left;
            border-bottom: 1px solid #334155;
          }}
          .details-table th {{
            color: #94a3b8;
            font-weight: 600;
            width: 35%;
          }}
          .details-table td {{
            color: #f1f5f9;
            font-weight: 500;
          }}
          .policy-badge {{
            display: inline-block;
            padding: 4px 8px;
            background-color: #7f1d1d;
            color: #fca5a5;
            border-radius: 4px;
            font-size: 12px;
            font-weight: 600;
            text-transform: uppercase;
          }}
          .footer {{
            background-color: #0f172a;
            padding: 20px;
            text-align: center;
            font-size: 12px;
            color: #64748b;
            border-top: 1px solid #334155;
          }}
        </style>
      </head>
      <body>
        <div class=""container"">
          <div class=""header {headerClass}"">
            <h1>{title}</h1>
          </div>
          <div class=""content"">
            <div class=""alert-box {alertClass}"">
              {message}
            </div>
            
            {busDetailsHtml}
 
            <p style=""color: #94a3b8; font-size: 14px; line-height: 1.6;"">
              Lütfen en kısa sürede acente sisteminden bu poliçeyi yenileyip web panelinden tarihini güncelleyin.
            </p>
          </div>
          <div class=""footer"">
            Sigorta Takip Otomasyonu &copy; 2026
          </div>
        </div>
      </body>
    </html>";
        }

        // Convenience overload: sends a single reminder with its own short-lived client.
        public async Task SendPolicyReminderAsync(Bus bus, string policyType, string endDate, int daysRemaining, Settings settings)
        {
            using var client = CreateSmtpClient(settings);
            await SendPolicyReminderAsync(client, bus, policyType, endDate, daysRemaining, settings);
        }

        // Sends a reminder over a caller-supplied client (the caller owns/disposes it),
        // so a batch run can reuse one SMTP connection.
        internal async Task SendPolicyReminderAsync(SmtpClient client, Bus bus, string policyType, string endDate, int daysRemaining, Settings settings)
        {
            var policyNames = new Dictionary<string, string>
            {
                { "trafik", "Zorunlu Trafik Sigortası" },
                { "kasko", "Kasko" },
                { "koltuk", "Koltuk Sigortası" }
            };

            var pName = policyNames.GetValueOrDefault(policyType, policyType);
            var plate = EscapeHtml(bus.Plate);

            string title, message, subject, remainingText;

            if (daysRemaining < 0)
            {
                int overdue = Math.Abs(daysRemaining);
                title = $"🚨 SİGORTA SÜRESİ DOLDU: {plate}";
                message = $"<strong>{plate}</strong> plakalı aracın <strong>{pName}</strong> süresi <strong>{overdue} gün önce</strong> ({EscapeHtml(endDate)}) dolmuştur!";
                subject = $"[Sigorta Uyarısı] {bus.Plate} - {pName} süresi doldu!";
                remainingText = $"{overdue} gün önce doldu";
            }
            else if (daysRemaining == 0)
            {
                title = $"🚨 SİGORTA BUGÜN BİTİYOR: {plate}";
                message = $"<strong>{plate}</strong> plakalı aracın <strong>{pName}</strong> süresi <strong>bugün</strong> ({EscapeHtml(endDate)}) doluyor!";
                subject = $"[Sigorta Uyarısı] {bus.Plate} - {pName} bugün bitiyor!";
                remainingText = "Bugün doluyor";
            }
            else
            {
                title = $"⚠️ SİGORTA YENİLEME HATIRLATMASI: {plate}";
                message = $"<strong>{plate}</strong> plakalı aracın <strong>{pName}</strong> süresinin dolmasına <strong>{daysRemaining} gün</strong> kaldı (Bitiş: {EscapeHtml(endDate)}).";
                subject = $"[Sigorta Hatırlatma] {bus.Plate} - {pName} bitişine {daysRemaining} gün";
                remainingText = $"{daysRemaining} gün kaldı";
            }

            var htmlContent = GenerateEmailTemplate(title, message, bus, policyType, endDate, remainingText);

            var mailMessage = new MailMessage
            {
                From = new MailAddress(settings.SenderEmail, !string.IsNullOrWhiteSpace(settings.SenderName) ? settings.SenderName : "Sigorta Takip"),
                Subject = subject,
                Body = htmlContent,
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8
            };
            mailMessage.To.Add(settings.ReceiverEmail);

            await client.SendMailAsync(mailMessage);
        }

        public async Task SendTestEmailAsync(Settings settings)
        {
            var title = "✓ Sigorta Takip Mail Bağlantı Testi";
            var message = "E-posta bildirim sisteminiz başarıyla yapılandırılmıştır. Sigorta bitiş günlerinde bu adrese uyarı mesajları gönderilecektir.";

            var sampleBus = new Bus
            {
                Plate = "34 TEST 9999",
                Brand = "Mercedes-Benz Travego (Test)",
                Operator = "Test Acentesi"
            };

            var htmlContent = GenerateEmailTemplate(title, message, sampleBus);

            using var client = CreateSmtpClient(settings);
            var mailMessage = new MailMessage
            {
                From = new MailAddress(settings.SenderEmail, !string.IsNullOrWhiteSpace(settings.SenderName) ? settings.SenderName : "Sigorta Takip"),
                Subject = "[Sigorta Takip] E-posta Bağlantı Testi",
                Body = htmlContent,
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8
            };
            mailMessage.To.Add(settings.ReceiverEmail);

            await client.SendMailAsync(mailMessage);
        }

        public async Task SendPasswordResetEmailAsync(string email, string token, Settings settings, string? requestOrigin = null)
        {
            using var client = CreateSmtpClient(settings);
            var baseUrl = !string.IsNullOrEmpty(requestOrigin) ? requestOrigin : "http://localhost:5173";
            var resetLink = $"{baseUrl}/?resetToken={token}";

            var htmlContent = $@"
    <!DOCTYPE html>
    <html>
      <head>
        <meta charset=""utf-8"">
        <style>
          body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background-color: #0f172a;
            color: #e2e8f0;
            margin: 0;
            padding: 20px;
          }}
          .container {{
            max-width: 600px;
            margin: 0 auto;
            background-color: #1e293b;
            border-radius: 16px;
            overflow: hidden;
            box-shadow: 0 10px 25px rgba(0,0,0,0.3);
            border: 1px solid #334155;
          }}
          .header {{
            background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%);
            padding: 30px;
            text-align: center;
          }}
          .header h1 {{
            margin: 0;
            font-size: 24px;
            font-weight: 700;
            color: #ffffff;
            letter-spacing: 0;
          }}
          .content {{
            padding: 30px;
            text-align: center;
          }}
          .btn {{
            display: inline-block;
            padding: 12px 30px;
            background-color: #3b82f6;
            color: #ffffff !important;
            text-decoration: none;
            border-radius: 8px;
            font-weight: bold;
            margin: 25px 0;
            box-shadow: 0 4px 12px rgba(59, 130, 246, 0.3);
          }}
          .btn:hover {{
            background-color: #2563eb;
          }}
          .footer {{
            background-color: #0f172a;
            padding: 20px;
            text-align: center;
            font-size: 12px;
            color: #64748b;
            border-top: 1px solid #334155;
          }}
        </style>
      </head>
      <body>
        <div class=""container"">
          <div class=""header"">
            <h1>Şifre Sıfırlama Talebi</h1>
          </div>
          <div class=""content"">
            <p style=""font-size: 16px; color: #f1f5f9; text-align: left;"">
              Merhaba,
            </p>
            <p style=""font-size: 14px; color: #94a3b8; line-height: 1.6; text-align: left;"">
              Sigorta Takip Otomasyon hesabınız için şifre sıfırlama talebinde bulundunuz. Aşağıdaki butona tıklayarak yeni şifrenizi belirleyebilirsiniz.
            </p>
            
            <a href=""{resetLink}"" class=""btn"" target=""_blank"" style=""color: #ffffff !important;"">Şifremi Sıfırla</a>
            
            <p style=""font-size: 12px; color: #64748b; margin-top: 15px; text-align: left;"">
              Bu bağlantı güvenlik nedeniyle <strong>1 saat</strong> boyunca geçerlidir.
            </p>
            <p style=""font-size: 12px; color: #64748b; text-align: left;"">
              Eğer bu talebi siz yapmadıysanız, lütfen bu e-postayı dikkate almayın. Şifreniz güvende kalacaktır.
            </p>
          </div>
          <div class=""footer"">
            Sigorta Takip Otomasyonu &copy; 2026
          </div>
        </div>
      </body>
    </html>";

            var mailMessage = new MailMessage
            {
                From = new MailAddress(settings.SenderEmail, !string.IsNullOrWhiteSpace(settings.SenderName) ? settings.SenderName : "Sigorta Takip"),
                Subject = "[Sigorta Takip] Şifre Sıfırlama Talebi",
                Body = htmlContent,
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8
            };
            mailMessage.To.Add(email);

            await client.SendMailAsync(mailMessage);
        }
    }
}
