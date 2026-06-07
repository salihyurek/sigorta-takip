const express = require('express');
const cors = require('cors');
const path = require('path');
const rateLimit = require('express-rate-limit');
const helmet = require('helmet');
const db = require('./db');
const { sendTestEmail, sendPasswordResetEmail } = require('./mail');
const { checkAllExpiries, initScheduler } = require('./scheduler');

const app = express();
const PORT = process.env.PORT || 5001;
const HOST = process.env.HOST || '127.0.0.1';

// Middlewares
app.use(cors());
app.use(helmet({ contentSecurityPolicy: false }));
app.use(express.json({ limit: '10mb' }));

// Initialize Scheduler
initScheduler();

const crypto = require('crypto');

// Session Store: token -> { email, role, createdAt }
const sessions = new Map();
const SESSION_TTL = 24 * 60 * 60 * 1000; // 24 hours

// Clean expired sessions periodically
setInterval(() => {
  const now = Date.now();
  for (const [token, session] of sessions.entries()) {
    if (now - session.createdAt > SESSION_TTL) {
      sessions.delete(token);
    }
  }
}, 60 * 60 * 1000); // Every hour

// Rate Limiters
const authLimiter = rateLimit({
  windowMs: 15 * 60 * 1000, // 15 minutes
  max: 10,
  message: { error: 'Çok fazla deneme yaptınız. Lütfen 15 dakika sonra tekrar deneyin.' },
  standardHeaders: true,
  legacyHeaders: false
});

const forgotPasswordLimiter = rateLimit({
  windowMs: 60 * 60 * 1000, // 1 hour
  max: 3,
  message: { error: 'Çok fazla şifre sıfırlama talebi gönderildi. Lütfen 1 saat sonra tekrar deneyin.' },
  standardHeaders: true,
  legacyHeaders: false
});

// Helper: Password complexity validation
function validatePasswordStrength(password) {
  if (!password || password.length < 8) {
    return 'Şifre en az 8 karakter uzunluğunda olmalıdır.';
  }
  if (!/[A-Z]/.test(password)) {
    return 'Şifre en az bir büyük harf içermelidir.';
  }
  if (!/[a-z]/.test(password)) {
    return 'Şifre en az bir küçük harf içermelidir.';
  }
  if (!/[0-9]/.test(password)) {
    return 'Şifre en az bir rakam içermelidir.';
  }
  return null; // Valid
}

// Helper: Date validation
function isValidDate(dateStr) {
  if (!dateStr || typeof dateStr !== 'string') return false;
  const regex = /^\d{4}-\d{2}-\d{2}$/;
  if (!regex.test(dateStr)) return false;
  const date = new Date(dateStr + 'T00:00:00');
  return !isNaN(date.getTime());
}

// Authentication Middleware
function requireAuth(req, res, next) {
  const authHeader = req.headers['authorization'];
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    return res.status(401).json({ error: 'Giriş yapmanız gerekmektedir.' });
  }
  
  const token = authHeader.split(' ')[1];
  const sessionData = sessions.get(token);
  
  if (!sessionData) {
    return res.status(401).json({ error: 'Oturum süresi dolmuş veya geçersiz token.' });
  }
  
  // Check session TTL
  if (Date.now() - sessionData.createdAt > SESSION_TTL) {
    sessions.delete(token);
    return res.status(401).json({ error: 'Oturum süreniz dolmuştur. Lütfen tekrar giriş yapın.' });
  }
  
  req.userEmail = sessionData.email;
  req.userRole = sessionData.role;
  next();
}

// Super Admin Middleware
function requireSuperAdmin(req, res, next) {
  if (req.userRole !== 'superadmin') {
    return res.status(403).json({ error: 'Bu işlem için yetkiniz bulunmamaktadır.' });
  }
  next();
}

// Apply Auth Middleware to protect route groups
app.use('/api/buses', requireAuth);
app.use('/api/settings', requireAuth);
app.use('/api/check-expiries', requireAuth);
app.use('/api/restore', requireAuth);

// Authentication Routes

// Login
app.post('/api/auth/login', authLimiter, (req, res) => {
  try {
    const { email, password } = req.body;
    if (!email || !password) {
      return res.status(400).json({ error: 'E-posta ve şifre gereklidir.' });
    }
    
    const data = db.readDb();
    const user = data.users.find(u => u.email.toLowerCase() === email.toLowerCase().trim());
    
    if (!user || !db.comparePassword(password, user.password)) {
      return res.status(401).json({ error: 'E-posta adresi veya şifre hatalı!' });
    }
    
    const token = crypto.randomBytes(32).toString('hex');
    sessions.set(token, { email: user.email, role: user.role, createdAt: Date.now() });
    
    res.json({ success: true, token, email: user.email, role: user.role });
  } catch (error) {
    res.status(500).json({ error: 'Giriş işlemi sırasında hata oluştu', details: error.message });
  }
});

// Logout
app.post('/api/auth/logout', requireAuth, (req, res) => {
  const authHeader = req.headers['authorization'];
  const token = authHeader.split(' ')[1];
  sessions.delete(token);
  res.json({ success: true });
});

// Me (Verify session)
app.get('/api/auth/me', requireAuth, (req, res) => {
  res.json({ success: true, email: req.userEmail, role: req.userRole });
});

// Change Password
app.post('/api/auth/change-password', requireAuth, (req, res) => {
  try {
    const { oldPassword, newPassword } = req.body;
    if (!oldPassword || !newPassword) {
      return res.status(400).json({ error: 'Eski ve yeni şifre gereklidir.' });
    }
    
    const passwordError = validatePasswordStrength(newPassword);
    if (passwordError) {
      return res.status(400).json({ error: passwordError });
    }
    
    const data = db.readDb();
    const userIndex = data.users.findIndex(u => u.email.toLowerCase() === req.userEmail.toLowerCase());
    
    if (userIndex === -1) {
      return res.status(404).json({ error: 'Kullanıcı bulunamadı.' });
    }
    
    const user = data.users[userIndex];
    if (!db.comparePassword(oldPassword, user.password)) {
      return res.status(400).json({ error: 'Eski şifreniz hatalı!' });
    }
    
    user.password = db.hashPassword(newPassword);
    db.writeDb(data);
    
    // Invalidate all sessions for this user
    for (const [sessionToken, session] of sessions.entries()) {
      if (session.email.toLowerCase() === user.email.toLowerCase()) {
        sessions.delete(sessionToken);
      }
    }
    
    res.json({ success: true, message: 'Şifreniz başarıyla güncellendi.' });
  } catch (error) {
    res.status(500).json({ error: 'Şifre güncellenirken hata oluştu', details: error.message });
  }
});

// Forgot Password Request
app.post('/api/auth/forgot-password', forgotPasswordLimiter, async (req, res) => {
  try {
    const { email } = req.body;
    if (!email) {
      return res.status(400).json({ error: 'E-posta adresi gereklidir.' });
    }

    const data = db.readDb();
    const userIndex = data.users.findIndex(u => u.email.toLowerCase() === email.toLowerCase().trim());

    if (userIndex === -1) {
      // Don't reveal whether the email exists
      return res.json({ success: true, message: 'Eğer bu e-posta adresi sistemde kayıtlıysa, şifre sıfırlama bağlantısı gönderilecektir.' });
    }

    // Check if SMTP settings are configured
    const settings = data.settings;
    if (!settings || !settings.smtpHost || !settings.smtpUser) {
      return res.status(400).json({ 
        error: 'Sistem e-posta (SMTP) ayarları henüz yapılmamış veya eksik. Şifre sıfırlama e-postası gönderilemiyor. Lütfen sistem yöneticiniz ile iletişime geçin.' 
      });
    }

    const user = data.users[userIndex];
    const resetToken = crypto.randomBytes(20).toString('hex');
    const resetTokenExpiry = Date.now() + 3600000; // 1 hour

    user.resetToken = resetToken;
    user.resetTokenExpiry = resetTokenExpiry;
    db.writeDb(data);

    // Send the email
    await sendPasswordResetEmail(user.email, resetToken, settings);
    res.json({ success: true, message: 'Eğer bu e-posta adresi sistemde kayıtlıysa, şifre sıfırlama bağlantısı gönderilecektir.' });
  } catch (error) {
    console.error('Forgot password error:', error);
    res.status(500).json({ error: 'Şifre sıfırlama işlemi başlatılamadı', details: error.message });
  }
});

// Reset Password Completion
app.post('/api/auth/reset-password', (req, res) => {
  try {
    const { token, password } = req.body;
    if (!token || !password) {
      return res.status(400).json({ error: 'Geçersiz istek. Sıfırlama kodu ve şifre gereklidir.' });
    }

    const passwordError = validatePasswordStrength(password);
    if (passwordError) {
      return res.status(400).json({ error: passwordError });
    }

    const data = db.readDb();
    const userIndex = data.users.findIndex(
      u => u.resetToken === token && u.resetTokenExpiry && u.resetTokenExpiry > Date.now()
    );

    if (userIndex === -1) {
      return res.status(400).json({ error: 'Geçersiz veya süresi dolmuş sıfırlama bağlantısı!' });
    }

    const user = data.users[userIndex];
    user.password = db.hashPassword(password);
    user.resetToken = null;
    user.resetTokenExpiry = null;
    
    db.writeDb(data);
    
    // Invalidate all sessions for this user
    for (const [sessionToken, session] of sessions.entries()) {
      if (session.email.toLowerCase() === user.email.toLowerCase()) {
        sessions.delete(sessionToken);
      }
    }
    
    res.json({ success: true, message: 'Şifreniz başarıyla yenilendi! Yeni şifrenizle giriş yapabilirsiniz.' });
  } catch (error) {
    res.status(500).json({ error: 'Şifre yenilenirken hata oluştu', details: error.message });
  }
});

// User Management Routes (Requires Admin Auth)

// Get all users (emails only)
app.get('/api/users', requireAuth, requireSuperAdmin, (req, res) => {
  try {
    const data = db.readDb();
    const usersList = data.users.map(u => ({ id: u.id, email: u.email, role: u.role }));
    res.json(usersList);
  } catch (error) {
    res.status(500).json({ error: 'Kullanıcılar listelenemedi', details: error.message });
  }
});

// Add an authorized email
app.post('/api/users', requireAuth, requireSuperAdmin, (req, res) => {
  try {
    const { email, password } = req.body;
    if (!email || !password) {
      return res.status(400).json({ error: 'E-posta ve şifre gereklidir.' });
    }
    
    const passwordError = validatePasswordStrength(password);
    if (passwordError) {
      return res.status(400).json({ error: passwordError });
    }
    
    const data = db.readDb();
    const emailExists = data.users.some(u => u.email.toLowerCase() === email.toLowerCase().trim());
    
    if (emailExists) {
      return res.status(400).json({ error: 'Bu e-posta adresi zaten yetkilendirilmiş!' });
    }
    
    const newUser = {
      id: 'user-' + Date.now(),
      email: email.toLowerCase().trim(),
      password: db.hashPassword(password),
      role: 'viewer', // Added users default to viewers
      resetToken: null,
      resetTokenExpiry: null
    };
    
    data.users.push(newUser);
    db.writeDb(data);
    res.status(201).json({ id: newUser.id, email: newUser.email });
  } catch (error) {
    res.status(500).json({ error: 'Kullanıcı eklenemedi', details: error.message });
  }
});

// Delete an authorized email
app.delete('/api/users/:email', requireAuth, requireSuperAdmin, (req, res) => {
  try {
    const { email } = req.params;
    const targetEmail = email.toLowerCase().trim();
    
    if (targetEmail === req.userEmail.toLowerCase()) {
      return res.status(400).json({ error: 'Kendi hesabınızı silemezsiniz!' });
    }
    
    const data = db.readDb();
    
    if (data.users.length <= 1) {
      return res.status(400).json({ error: 'Sistemde en az bir yönetici bulunmalıdır.' });
    }
    
    const userIndex = data.users.findIndex(u => u.email.toLowerCase() === targetEmail);
    if (userIndex === -1) {
      return res.status(404).json({ error: 'Kullanıcı bulunamadı.' });
    }
    
    data.users.splice(userIndex, 1);
    db.writeDb(data);
    res.json({ success: true, message: 'Kullanıcı yetkisi kaldırıldı.' });
  } catch (error) {
    res.status(500).json({ error: 'Kullanıcı silinemedi', details: error.message });
  }
});

// API Endpoints

// 1. Get all buses
app.get('/api/buses', (req, res) => {
  try {
    const data = db.readDb();
    res.json(data.buses || []);
  } catch (error) {
    res.status(500).json({ error: 'Failed to read buses', details: error.message });
  }
});

// 2. Add a new bus
app.post('/api/buses', requireSuperAdmin, (req, res) => {
  try {
    const { plate, brand, operator, policies } = req.body;
    
    if (!plate || !brand || !operator || !policies) {
      return res.status(400).json({ error: 'All fields are required' });
    }

    // Validate dates
    for (const key of ['trafik', 'kasko', 'koltuk']) {
      if (!policies[key] || !isValidDate(policies[key].startDate) || !isValidDate(policies[key].endDate)) {
        return res.status(400).json({ error: `Geçersiz tarih formatı: ${key}` });
      }
    }

    const data = db.readDb();
    
    // Check if plate already exists
    const normalizedPlate = plate.toUpperCase().replace(/\s+/g, '').trim();
    const plateExists = data.buses.some(
      b => b.plate.toUpperCase().replace(/\s+/g, '').trim() === normalizedPlate
    );
    
    if (plateExists) {
      return res.status(400).json({ error: 'Bu plakaya sahip bir araç zaten kayıtlı!' });
    }

    const newBus = {
      id: 'bus-' + Date.now(),
      plate: plate.toUpperCase().trim(),
      brand: brand.trim(),
      operator: operator.trim(),
      policies: {
        trafik: {
          startDate: policies.trafik.startDate,
          endDate: policies.trafik.endDate,
          lastEmailedDate: null
        },
        kasko: {
          startDate: policies.kasko.startDate,
          endDate: policies.kasko.endDate,
          lastEmailedDate: null
        },
        koltuk: {
          startDate: policies.koltuk.startDate,
          endDate: policies.koltuk.endDate,
          lastEmailedDate: null
        }
      }
    };

    data.buses.unshift(newBus); // Add to the beginning of the list
    db.writeDb(data);
    res.status(201).json(newBus);
  } catch (error) {
    res.status(500).json({ error: 'Failed to add bus', details: error.message });
  }
});

// 3. Update an existing bus
app.put('/api/buses/:id', requireSuperAdmin, (req, res) => {
  try {
    const { id } = req.params;
    const { plate, brand, operator, policies } = req.body;
    
    if (!plate || !brand || !operator || !policies) {
      return res.status(400).json({ error: 'All fields are required' });
    }

    // Validate dates
    for (const key of ['trafik', 'kasko', 'koltuk']) {
      if (!policies[key] || !isValidDate(policies[key].startDate) || !isValidDate(policies[key].endDate)) {
        return res.status(400).json({ error: `Geçersiz tarih formatı: ${key}` });
      }
    }

    const data = db.readDb();
    const busIndex = data.buses.findIndex(b => b.id === id);

    if (busIndex === -1) {
      return res.status(404).json({ error: 'Bus not found' });
    }

    // Check if new plate clashes with another bus
    const normalizedPlate = plate.toUpperCase().replace(/\s+/g, '').trim();
    const plateExists = data.buses.some(
      b => b.id !== id && b.plate.toUpperCase().replace(/\s+/g, '').trim() === normalizedPlate
    );

    if (plateExists) {
      return res.status(400).json({ error: 'Bu plakaya sahip başka bir araç zaten kayıtlı!' });
    }

    // Retain previous lastEmailedDate if the endDate hasn't changed, otherwise reset it
    const updatedPolicies = {};
    for (let key of ['trafik', 'kasko', 'koltuk']) {
      const oldPolicy = data.buses[busIndex].policies[key];
      const newPolicy = policies[key];
      
      updatedPolicies[key] = {
        startDate: newPolicy.startDate,
        endDate: newPolicy.endDate,
        lastEmailedDate: (oldPolicy && oldPolicy.endDate === newPolicy.endDate) 
          ? oldPolicy.lastEmailedDate 
          : null
      };
    }

    data.buses[busIndex] = {
      ...data.buses[busIndex],
      plate: plate.toUpperCase().trim(),
      brand: brand.trim(),
      operator: operator.trim(),
      policies: updatedPolicies
    };

    db.writeDb(data);
    res.json(data.buses[busIndex]);
  } catch (error) {
    res.status(500).json({ error: 'Failed to update bus', details: error.message });
  }
});

// 4. Delete a bus
app.delete('/api/buses/:id', requireSuperAdmin, (req, res) => {
  try {
    const { id } = req.params;
    const data = db.readDb();
    
    const initialLength = data.buses.length;
    data.buses = data.buses.filter(b => b.id !== id);
    
    if (data.buses.length === initialLength) {
      return res.status(404).json({ error: 'Bus not found' });
    }

    db.writeDb(data);
    res.json({ success: true, message: 'Bus deleted successfully' });
  } catch (error) {
    res.status(500).json({ error: 'Failed to delete bus', details: error.message });
  }
});

// 5. Get SMTP settings (masking password)
app.get('/api/settings', (req, res) => {
  try {
    const data = db.readDb();
    const settings = { ...data.settings };
    
    // Mask password before sending to client
    if (settings.smtpPass) {
      settings.smtpPass = '********';
    }
    
    res.json(settings);
  } catch (error) {
    res.status(500).json({ error: 'Failed to read settings', details: error.message });
  }
});

// 6. Save SMTP settings
app.post('/api/settings', requireSuperAdmin, (req, res) => {
  try {
    const newSettings = req.body;
    const data = db.readDb();

    // If client sends masked password, keep the old password
    if (newSettings.smtpPass === '********') {
      newSettings.smtpPass = data.settings.smtpPass || '';
    }

    data.settings = {
      smtpHost: (newSettings.smtpHost || '').trim(),
      smtpPort: parseInt(newSettings.smtpPort) || 587,
      smtpUser: (newSettings.smtpUser || '').trim(),
      smtpPass: newSettings.smtpPass || '',
      senderName: (newSettings.senderName || '').trim(),
      senderEmail: (newSettings.senderEmail || '').trim(),
      receiverEmail: (newSettings.receiverEmail || '').trim(),
      enableEmails: !!newSettings.enableEmails
    };

    db.writeDb(data);
    
    // Mask password in response
    const responseSettings = { ...data.settings };
    if (responseSettings.smtpPass) {
      responseSettings.smtpPass = '********';
    }
    
    res.json({ success: true, settings: responseSettings });
  } catch (error) {
    res.status(500).json({ error: 'Failed to save settings', details: error.message });
  }
});

// 7. Send Test Email
app.post('/api/settings/test-email', requireSuperAdmin, async (req, res) => {
  try {
    const testSettings = req.body;
    const data = db.readDb();

    // If client sends masked password, load the existing one from database
    if (testSettings.smtpPass === '********') {
      testSettings.smtpPass = data.settings.smtpPass || '';
    }

    await sendTestEmail(testSettings);
    res.json({ success: true, message: 'Test email sent successfully!' });
  } catch (error) {
    console.error('Test email sending error:', error);
    res.status(500).json({ error: 'Failed to send test email', details: error.message });
  }
});

// 8. Manually check and send notifications
app.post('/api/check-expiries', requireSuperAdmin, async (req, res) => {
  try {
    const result = await checkAllExpiries();
    res.json(result);
  } catch (error) {
    res.status(500).json({ error: 'Failed to trigger check', details: error.message });
  }
});

// 9. Restore database from backup
app.post('/api/restore', requireSuperAdmin, (req, res) => {
  try {
    const { settings, buses } = req.body;
    
    if (!buses || !Array.isArray(buses)) {
      return res.status(400).json({ error: 'Geçersiz yedek dosyası' });
    }

    const currentData = db.readDb();
    
    const data = {
      settings: settings || currentData.settings,
      buses: buses,
      users: currentData.users  // Preserve users!
    };

    db.writeDb(data);
    res.json({ success: true, message: 'Veritabanı başarıyla geri yüklendi' });
  } catch (error) {
    res.status(500).json({ error: 'Geri yükleme başarısız oldu', details: error.message });
  }
});

// Serve static assets in production
app.use(express.static(path.join(__dirname, '..', 'dist')));

app.get('*', (req, res) => {
  res.sendFile(path.join(__dirname, '..', 'dist', 'index.html'));
});

// Start Server
app.listen(PORT, HOST, () => {
  console.log(`[Server] running on http://${HOST}:${PORT}`);
});
