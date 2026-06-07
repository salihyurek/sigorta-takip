const nodemailer = require('nodemailer');

// HTML sanitization helper to prevent XSS
function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

// Helper to create transport based on user settings
function createTransporter(settings) {
  if (!settings.smtpHost || !settings.smtpPort || !settings.smtpUser || !settings.smtpPass) {
    throw new Error('SMTP settings are incomplete. Please configure SMTP settings in the App.');
  }

  // Determine if secure connection should be used based on port
  const secure = parseInt(settings.smtpPort) === 465;

  return nodemailer.createTransport({
    host: settings.smtpHost,
    port: parseInt(settings.smtpPort),
    secure: secure,
    auth: {
      user: settings.smtpUser,
      pass: settings.smtpPass
    },
    tls: {
      // Do not fail on invalid certs (common with custom SMTP configurations)
      rejectUnauthorized: false
    }
  });
}

// Helper to generate a styled HTML email template
function generateEmailTemplate({ title, message, busDetails, policyType, expiryDate }) {
  const policyNames = {
    trafik: 'Zorunlu Trafik Sigortası',
    kasko: 'Kasko',
    koltuk: 'Koltuk Sigortası'
  };

  return `
    <!DOCTYPE html>
    <html>
      <head>
        <meta charset="utf-8">
        <style>
          body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background-color: #0f172a;
            color: #e2e8f0;
            margin: 0;
            padding: 20px;
          }
          .container {
            max-width: 600px;
            margin: 0 auto;
            background-color: #1e293b;
            border-radius: 16px;
            overflow: hidden;
            box-shadow: 0 10px 25px rgba(0,0,0,0.3);
            border: 1px solid #334155;
          }
          .header {
            background: linear-gradient(135deg, #ef4444 0%, #b91c1c 100%);
            padding: 30px;
            text-align: center;
          }
          .header.test {
            background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%);
          }
          .header h1 {
            margin: 0;
            font-size: 24px;
            font-weight: 700;
            color: #ffffff;
            letter-spacing: 0;
          }
          .content {
            padding: 30px;
          }
          .alert-box {
            background-color: rgba(239, 68, 68, 0.1);
            border-left: 4px solid #ef4444;
            padding: 15px;
            border-radius: 4px;
            margin-bottom: 25px;
            color: #fca5a5;
          }
          .alert-box.test {
            background-color: rgba(59, 130, 246, 0.1);
            border-left: 4px solid #3b82f6;
            color: #93c5fd;
          }
          .details-table {
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 25px;
          }
          .details-table th, .details-table td {
            padding: 12px;
            text-align: left;
            border-bottom: 1px solid #334155;
          }
          .details-table th {
            color: #94a3b8;
            font-weight: 600;
            width: 35%;
          }
          .details-table td {
            color: #f1f5f9;
            font-weight: 500;
          }
          .policy-badge {
            display: inline-block;
            padding: 4px 8px;
            background-color: #7f1d1d;
            color: #fca5a5;
            border-radius: 4px;
            font-size: 12px;
            font-weight: 600;
            text-transform: uppercase;
          }
          .footer {
            background-color: #0f172a;
            padding: 20px;
            text-align: center;
            font-size: 12px;
            color: #64748b;
            border-top: 1px solid #334155;
          }
          .btn {
            display: inline-block;
            padding: 12px 24px;
            background-color: #ef4444;
            color: #ffffff !important;
            text-decoration: none;
            border-radius: 8px;
            font-weight: bold;
            margin-top: 10px;
            text-align: center;
          }
          .btn.test {
            background-color: #3b82f6;
          }
        </style>
      </head>
      <body>
        <div class="container">
          <div class="header ${policyType ? '' : 'test'}">
            <h1>${title}</h1>
          </div>
          <div class="content">
            <div class="alert-box ${policyType ? '' : 'test'}">
              ${message}
            </div>
            
            ${busDetails ? `
              <table class="details-table">
                <tr>
                  <th>Plaka</th>
                  <td>${escapeHtml(busDetails.plate)}</td>
                </tr>
                <tr>
                  <th>Marka / Model</th>
                  <td>${escapeHtml(busDetails.brand)}</td>
                </tr>
                <tr>
                  <th>Acente / Firma</th>
                  <td>${escapeHtml(busDetails.operator)}</td>
                </tr>
                ${policyType ? `
                  <tr>
                    <th>Biten Sigorta</th>
                    <td><span class="policy-badge">${policyNames[policyType]}</span></td>
                  </tr>
                  <tr>
                    <th>Bitiş Tarihi</th>
                    <td><strong>${expiryDate}</strong></td>
                  </tr>
                ` : ''}
              </table>
            ` : ''}

            <p style="color: #94a3b8; font-size: 14px; line-height: 1.6;">
              Lütfen en kısa sürede acente sisteminden bu poliçeyi yenileyip web panelinden tarihini güncelleyin.
            </p>
          </div>
          <div class="footer">
            Sigorta Takip Otomasyonu &copy; 2026
          </div>
        </div>
      </body>
    </html>
  `;
}

// Send Expiry Alert Email
async function sendExpiryAlert(bus, policyType, endDate, settings) {
  const policyNames = {
    trafik: 'Zorunlu Trafik Sigortası',
    kasko: 'Kasko',
    koltuk: 'Koltuk Sigortası'
  };

  const transporter = createTransporter(settings);
  const title = `🚨 SİGORTA SÜRESİ DOLDU: ${escapeHtml(bus.plate)}`;
  const message = `<strong>${escapeHtml(bus.plate)}</strong> plakalı aracın <strong>${policyNames[policyType]}</strong> süresi bugün (${escapeHtml(endDate)}) dolmuştur!`;

  const htmlContent = generateEmailTemplate({
    title,
    message,
    busDetails: bus,
    policyType,
    expiryDate: endDate
  });

  const mailOptions = {
    from: `"${settings.senderName || 'Sigorta Takip'}" <${settings.senderEmail}>`,
    to: settings.receiverEmail,
    subject: `[Sigorta Uyarısı] ${bus.plate} - ${policyNames[policyType]} Bitti!`,
    html: htmlContent
  };

  return transporter.sendMail(mailOptions);
}

// Send Test Email
async function sendTestEmail(settings) {
  const transporter = createTransporter(settings);
  const title = `✓ Sigorta Takip Mail Bağlantı Testi`;
  const message = `E-posta bildirim sisteminiz başarıyla yapılandırılmıştır. Sigorta bitiş günlerinde bu adrese uyarı mesajları gönderilecektir.`;

  const sampleBus = {
    plate: '34 TEST 9999',
    brand: 'Mercedes-Benz Travego (Test)',
    operator: 'Test Acentesi'
  };

  const htmlContent = generateEmailTemplate({
    title,
    message,
    busDetails: sampleBus,
    policyType: null,
    expiryDate: null
  });

  const mailOptions = {
    from: `"${settings.senderName || 'Sigorta Takip'}" <${settings.senderEmail}>`,
    to: settings.receiverEmail,
    subject: `[Sigorta Takip] E-posta Bağlantı Testi`,
    html: htmlContent
  };

  return transporter.sendMail(mailOptions);
}

// Send Password Reset Email
async function sendPasswordResetEmail(email, token, settings) {
  const transporter = createTransporter(settings);
  const baseUrl = settings.appUrl || process.env.APP_URL || 'http://localhost:5173';
  const resetLink = `${baseUrl}/?resetToken=${token}`;
  
  const htmlContent = `
    <!DOCTYPE html>
    <html>
      <head>
        <meta charset="utf-8">
        <style>
          body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background-color: #0f172a;
            color: #e2e8f0;
            margin: 0;
            padding: 20px;
          }
          .container {
            max-width: 600px;
            margin: 0 auto;
            background-color: #1e293b;
            border-radius: 16px;
            overflow: hidden;
            box-shadow: 0 10px 25px rgba(0,0,0,0.3);
            border: 1px solid #334155;
          }
          .header {
            background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%);
            padding: 30px;
            text-align: center;
          }
          .header h1 {
            margin: 0;
            font-size: 24px;
            font-weight: 700;
            color: #ffffff;
            letter-spacing: 0;
          }
          .content {
            padding: 30px;
            text-align: center;
          }
          .btn {
            display: inline-block;
            padding: 12px 30px;
            background-color: #3b82f6;
            color: #ffffff !important;
            text-decoration: none;
            border-radius: 8px;
            font-weight: bold;
            margin: 25px 0;
            box-shadow: 0 4px 12px rgba(59, 130, 246, 0.3);
          }
          .btn:hover {
            background-color: #2563eb;
          }
          .footer {
            background-color: #0f172a;
            padding: 20px;
            text-align: center;
            font-size: 12px;
            color: #64748b;
            border-top: 1px solid #334155;
          }
        </style>
      </head>
      <body>
        <div class="container">
          <div class="header">
            <h1>Şifre Sıfırlama Talebi</h1>
          </div>
          <div class="content">
            <p style="font-size: 16px; color: #f1f5f9; text-align: left;">
              Merhaba,
            </p>
            <p style="font-size: 14px; color: #94a3b8; line-height: 1.6; text-align: left;">
              Sigorta Takip Otomasyon hesabınız için şifre sıfırlama talebinde bulundunuz. Aşağıdaki butona tıklayarak yeni şifrenizi belirleyebilirsiniz.
            </p>
            
            <a href="${resetLink}" class="btn" target="_blank" style="color: #ffffff !important;">Şifremi Sıfırla</a>
            
            <p style="font-size: 12px; color: #64748b; margin-top: 15px; text-align: left;">
              Bu bağlantı güvenlik nedeniyle <strong>1 saat</strong> boyunca geçerlidir.
            </p>
            <p style="font-size: 12px; color: #64748b; text-align: left;">
              Eğer bu talebi siz yapmadıysanız, lütfen bu e-postayı dikkate almayın. Şifreniz güvende kalacaktır.
            </p>
          </div>
          <div class="footer">
            Sigorta Takip Otomasyonu &copy; 2026
          </div>
        </div>
      </body>
    </html>
  `;

  const mailOptions = {
    from: `"${settings.senderName || 'Sigorta Takip'}" <${settings.senderEmail}>`,
    to: email,
    subject: `[Sigorta Takip] Şifre Sıfırlama Talebi`,
    html: htmlContent
  };

  return transporter.sendMail(mailOptions);
}

module.exports = {
  sendExpiryAlert,
  sendTestEmail,
  sendPasswordResetEmail
};
