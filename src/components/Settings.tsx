import { useState, useEffect, useCallback, type FormEvent, type ChangeEvent } from 'react';
import type { BackupData, Bus, SMTPConfig } from '../types';
import { Mail, Shield, Save, RefreshCw, Download, Upload, AlertCircle, CheckCircle, UserPlus, Key, Trash2, ShieldAlert } from 'lucide-react';
import { exportBusesToExcel, importBusesFromExcel } from '../utils/excelHelpers';

interface SettingsProps {
  settings: SMTPConfig;
  onSaveSettings: (settings: SMTPConfig) => Promise<void>;
  onTestEmail: (settings: SMTPConfig) => Promise<void>;
  onRestoreDb: (data: BackupData) => Promise<void>;
  onImportExcel: (importedBuses: Omit<Bus, 'id'>[]) => Promise<void>;
  busesData: Bus[];
  currentUserEmail: string;
  currentUserRole: string;
  onChangePassword: (oldPass: string, newPass: string) => Promise<void>;
}

interface User {
  id: string;
  email: string;
  role?: string;
  isBootstrap?: boolean;
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback;
}

const safeJson = async (res: Response) => {
  try { return await res.json(); } catch { return null; }
};

export const Settings = ({
  settings,
  onSaveSettings,
  onTestEmail,
  onRestoreDb,
  onImportExcel,
  busesData,
  currentUserEmail,
  currentUserRole,
  onChangePassword
}: SettingsProps) => {
  const isSuperAdmin = currentUserRole === 'superadmin';

  // SMTP Config state
  const [smtpHost, setSmtpHost] = useState('');
  const [smtpPort, setSmtpPort] = useState(587);
  const [smtpUser, setSmtpUser] = useState('');
  const [smtpPass, setSmtpPass] = useState('');
  const [senderName, setSenderName] = useState('');
  const [senderEmail, setSenderEmail] = useState('');
  const [receiverEmail, setReceiverEmail] = useState('');
  const [enableEmails, setEnableEmails] = useState(false);

  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [settingsStatus, setSettingsStatus] = useState<{ success: boolean; message: string } | null>(null);
  
  const [restoreError, setRestoreError] = useState<string | null>(null);
  const [restoreSuccess, setRestoreSuccess] = useState<string | null>(null);

  // User Management state
  const [users, setUsers] = useState<User[]>([]);
  const [newAdminEmail, setNewAdminEmail] = useState('');
  const [newAdminPassword, setNewAdminPassword] = useState('');
  const [newAdminRole, setNewAdminRole] = useState<'viewer' | 'superadmin'>('viewer');
  const [userActionError, setUserActionError] = useState<string | null>(null);
  const [userActionSuccess, setUserActionSuccess] = useState<string | null>(null);
  const [addingUser, setAddingUser] = useState(false);

  const authHeader = () => {
    const token = localStorage.getItem('token') || sessionStorage.getItem('token');
    return { 'Authorization': `Bearer ${token}` };
  };

  // Password Change state
  const [oldPassword, setOldPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [passError, setPassError] = useState<string | null>(null);
  const [passSuccess, setPassSuccess] = useState<string | null>(null);
  const [changingPass, setChangingPass] = useState(false);

  // Fetch all authorized admins
  const fetchUsers = useCallback(async () => {
    if (!isSuperAdmin) return;
    try {
      const token = localStorage.getItem('token') || sessionStorage.getItem('token');
      const res = await fetch('/api/users', {
        headers: { 'Authorization': `Bearer ${token}` }
      });
      if (res.ok) {
        const data = await res.json();
        setUsers(data);
      }
    } catch (err) {
      console.error('Kullanıcılar yüklenemedi:', err);
    }
  }, [isSuperAdmin]);

  useEffect(() => {
    if (settings) {
      setSmtpHost(settings.smtpHost || '');
      setSmtpPort(settings.smtpPort || 587);
      setSmtpUser(settings.smtpUser || '');
      setSmtpPass(settings.smtpPass || '');
      setSenderName(settings.senderName || 'Sigorta Takip');
      setSenderEmail(settings.senderEmail || '');
      setReceiverEmail(settings.receiverEmail || '');
      setEnableEmails(!!settings.enableEmails);
    }
    
    if (isSuperAdmin) {
      fetchUsers();
    }
  }, [settings, isSuperAdmin, fetchUsers]);

  const getFormSettings = (): SMTPConfig => ({
    smtpHost: smtpHost.trim(),
    smtpPort: Number(smtpPort),
    smtpUser: smtpUser.trim(),
    smtpPass: smtpPass,
    senderName: senderName.trim(),
    senderEmail: senderEmail.trim(),
    receiverEmail: receiverEmail.trim(),
    enableEmails
  });

  const handleSave = async (e: FormEvent) => {
    e.preventDefault();
    if (!isSuperAdmin) return;
    setSaving(true);
    setSettingsStatus(null);
    try {
      await onSaveSettings(getFormSettings());
      setSettingsStatus({ success: true, message: 'Ayarlar başarıyla kaydedildi!' });
    } catch (err: unknown) {
      setSettingsStatus({ success: false, message: getErrorMessage(err, 'Ayarlar kaydedilemedi.') });
    } finally {
      setSaving(false);
    }
  };

  const handleTestEmail = async () => {
    if (!isSuperAdmin) return;
    setTesting(true);
    setTestResult(null);
    try {
      await onTestEmail(getFormSettings());
      setTestResult({ success: true, message: 'Test e-postası başarıyla gönderildi! Lütfen alıcı posta kutusunu kontrol edin.' });
    } catch (err: unknown) {
      setTestResult({ success: false, message: getErrorMessage(err, 'Test e-postası gönderilemedi.') });
    } finally {
      setTesting(false);
    }
  };

  // Add a new manager
  const handleAddUser = async (e: FormEvent) => {
    e.preventDefault();
    if (!isSuperAdmin) return;
    setUserActionError(null);
    setUserActionSuccess(null);

    if (!newAdminEmail || !newAdminPassword) {
      setUserActionError('Lütfen e-posta ve şifre girin.');
      return;
    }

    try {
      setAddingUser(true);
      const res = await fetch('/api/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...authHeader() },
        body: JSON.stringify({ email: newAdminEmail, password: newAdminPassword, role: newAdminRole })
      });

      const data = await safeJson(res);
      if (!res.ok) throw new Error(data?.error || 'Kullanıcı eklenemedi.');

      const roleLabel = newAdminRole === 'superadmin' ? 'Yönetici' : 'Gözlemci';
      setUserActionSuccess(`"${newAdminEmail}" başarıyla ${roleLabel} olarak eklendi.`);
      setNewAdminEmail('');
      setNewAdminPassword('');
      setNewAdminRole('viewer');
      fetchUsers();
    } catch (err: unknown) {
      setUserActionError(getErrorMessage(err, 'Kullanıcı eklenemedi.'));
    } finally {
      setAddingUser(false);
    }
  };

  // Promote/demote an existing user
  const handleChangeRole = async (email: string, role: 'viewer' | 'superadmin') => {
    if (!isSuperAdmin) return;
    setUserActionError(null);
    setUserActionSuccess(null);

    const roleLabel = role === 'superadmin' ? 'Yönetici' : 'Gözlemci';
    if (!window.confirm(`"${email}" kullanıcısının yetkisi "${roleLabel}" olarak değiştirilsin mi? Bu kullanıcının oturumu sonlandırılacaktır.`)) {
      return;
    }

    try {
      const res = await fetch(`/api/users/${encodeURIComponent(email)}/role`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', ...authHeader() },
        body: JSON.stringify({ role })
      });
      const data = await safeJson(res);
      if (!res.ok) throw new Error(data?.error || 'Yetki güncellenemedi.');

      setUserActionSuccess(`"${email}" artık ${roleLabel}.`);
      fetchUsers();
    } catch (err: unknown) {
      setUserActionError(getErrorMessage(err, 'Yetki güncellenemedi.'));
    }
  };

  // Remove manager
  const handleDeleteUser = async (email: string) => {
    if (!isSuperAdmin) return;
    if (email.toLowerCase() === currentUserEmail.toLowerCase()) {
      alert('Kendi hesabınızı silemezsiniz!');
      return;
    }

    if (window.confirm(`"${email}" kullanıcısının erişim yetkisini kaldırmak istediğinizden emin misiniz?`)) {
      try {
        const res = await fetch(`/api/users/${encodeURIComponent(email)}`, {
          method: 'DELETE',
          headers: { ...authHeader() }
        });

        const data = await safeJson(res);
        if (!res.ok) throw new Error(data?.error || 'Kullanıcı silinemedi.');

        fetchUsers();
      } catch (err: unknown) {
        alert(getErrorMessage(err, 'Kullanıcı silinemedi.'));
      }
    }
  };

  // Change Password
  const handleChangePassSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setPassError(null);
    setPassSuccess(null);

    if (!oldPassword || !newPassword || !confirmPassword) {
      setPassError('Lütfen tüm şifre alanlarını doldurun.');
      return;
    }

    if (newPassword !== confirmPassword) {
      setPassError('Yeni şifreler uyuşmuyor.');
      return;
    }

    if (newPassword.length < 8) {
      setPassError('Yeni şifre en az 8 karakter uzunluğunda olmalıdır.');
      return;
    }

    if (!/[A-Z]/.test(newPassword) || !/[a-z]/.test(newPassword) || !/[0-9]/.test(newPassword)) {
      setPassError('Şifre en az bir büyük harf, bir küçük harf ve bir rakam içermelidir.');
      return;
    }

    try {
      setChangingPass(true);
      await onChangePassword(oldPassword, newPassword);
      setPassSuccess('Şifreniz başarıyla değiştirildi.');
      setOldPassword('');
      setNewPassword('');
      setConfirmPassword('');
    } catch (err: unknown) {
      setPassError(getErrorMessage(err, 'Şifre değiştirilemedi.'));
    } finally {
      setChangingPass(false);
    }
  };

  // Download DB as Backup
  const handleBackupDownload = () => {
    if (!isSuperAdmin) return;
    const backupData = {
      settings: getFormSettings(),
      buses: busesData
    };
    
    const dataStr = "data:text/json;charset=utf-8," + encodeURIComponent(JSON.stringify(backupData, null, 2));
    const downloadAnchor = document.createElement('a');
    downloadAnchor.setAttribute("href", dataStr);
    downloadAnchor.setAttribute("download", `sigorta_takip_yedek_${new Date().toISOString().split('T')[0]}.json`);
    document.body.appendChild(downloadAnchor);
    downloadAnchor.click();
    downloadAnchor.remove();
  };

  // Restore DB from Backup File
  const handleBackupUpload = (e: ChangeEvent<HTMLInputElement>) => {
    if (!isSuperAdmin) return;
    setRestoreError(null);
    setRestoreSuccess(null);
    
    const file = e.target.files?.[0];
    if (!file) return;

    const reader = new FileReader();
    reader.onload = async (event) => {
      try {
        const parsed = JSON.parse(event.target?.result as string);
        
        if (!parsed.buses || !Array.isArray(parsed.buses)) {
          setRestoreError('Geçersiz yedek dosyası! "buses" dizisi bulunamadı.');
          return;
        }

        if (window.confirm('Bu yedeği yüklemek mevcut tüm verilerinizi (araçlar ve ayarlar) silecektir. Devam etmek istiyor musunuz?')) {
          await onRestoreDb(parsed);
          setRestoreSuccess('Veritabanı yedekten başarıyla geri yüklendi!');
          
          // Clear file input
          e.target.value = '';
        }
      } catch {
        setRestoreError('Dosya okuma hatası! Lütfen geçerli bir JSON yedek dosyası seçin.');
      }
    };
    reader.readAsText(file);
  };

  // Excel File Upload States & Handlers
  const [excelError, setExcelError] = useState<string | null>(null);
  const [excelSuccess, setExcelSuccess] = useState<string | null>(null);

  const handleExcelExport = async () => {
    if (!isSuperAdmin) return;
    try {
      await exportBusesToExcel(busesData);
    } catch (err: unknown) {
      alert(getErrorMessage(err, 'Excel aktarımı başarısız oldu.'));
    }
  };

  const handleExcelImport = async (e: ChangeEvent<HTMLInputElement>) => {
    if (!isSuperAdmin) return;
    setExcelError(null);
    setExcelSuccess(null);

    const file = e.target.files?.[0];
    if (!file) return;

    try {
      const parsedBuses = await importBusesFromExcel(file);
      if (window.confirm(`${parsedBuses.length} adet araç verisi Excel'den içe aktarılacak (mevcut plakalı araçlar güncellenecek, yenileri eklenecektir). Onaylıyor musunuz?`)) {
        await onImportExcel(parsedBuses);
        setExcelSuccess('Excel verileri başarıyla içe aktarıldı!');
        e.target.value = ''; // Reset input
      }
    } catch (err: unknown) {
      setExcelError(getErrorMessage(err, 'Excel içe aktarma hatası.'));
      e.target.value = ''; // Reset input
    }
  };

  // 1. View-only (Viewer) layout: Displays only password change form card
  if (!isSuperAdmin) {
    return (
      <div style={{ maxWidth: '600px', margin: '2rem auto' }}>
        <div className="card settings-card">
          <h2 style={{ fontFamily: 'var(--font-display)', fontWeight: 700, fontSize: '1.5rem', display: 'flex', alignItems: 'center', gap: '0.5rem', borderBottom: '1px solid var(--border-color)', paddingBottom: '0.75rem' }}>
            <Key size={24} style={{ color: 'var(--primary)' }} />
            Şifre Değiştir
          </h2>
          
          <div style={{ display: 'flex', gap: '0.5rem', padding: '0.75rem', background: 'rgba(255, 255, 255, 0.02)', borderRadius: 'var(--radius-sm)', border: '1px solid var(--border-color)', fontSize: '0.85rem', color: 'var(--text-secondary)' }}>
            <ShieldAlert size={18} style={{ color: 'var(--warning)', flexShrink: 0 }} />
            <span>
              Hesabınız <strong>Gözlemci (Viewer)</strong> yetkisine sahiptir. Yalnızca şifrenizi değiştirebilirsiniz. Sigorta verileri veya genel sistem ayarları üzerinde değişiklik yetkiniz bulunmamaktadır.
            </span>
          </div>

          {passError && <div style={{ padding: '0.75rem', background: 'var(--danger-bg)', border: '1px solid var(--danger-border)', color: 'var(--danger)', borderRadius: '4px', fontSize: '0.85rem' }}>{passError}</div>}
          {passSuccess && <div style={{ padding: '0.75rem', background: 'var(--success-bg)', border: '1px solid var(--success-border)', color: 'var(--success)', borderRadius: '4px', fontSize: '0.85rem' }}>{passSuccess}</div>}

          <form onSubmit={handleChangePassSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem', marginTop: '0.5rem' }}>
            <div className="form-group">
              <label className="form-label">Eski Şifre</label>
              <input
                type="password"
                className="form-control"
                value={oldPassword}
                onChange={(e) => setOldPassword(e.target.value)}
                required
              />
            </div>
            <div className="form-group">
              <label className="form-label">Yeni Şifre</label>
              <input
                type="password"
                className="form-control"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                required
              />
            </div>
            <div className="form-group">
              <label className="form-label">Yeni Şifre (Tekrar)</label>
              <input
                type="password"
                className="form-control"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                required
              />
            </div>
            <button type="submit" className="btn-primary" disabled={changingPass} style={{ justifyContent: 'center', padding: '0.75rem' }}>
              Şifreyi Güncelle
            </button>
          </form>
        </div>
      </div>
    );
  }

  // 2. Full Super Admin Layout
  return (
    <div className="settings-container">
      {/* SMTP Form Card & User Settings Card */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: '2rem' }}>
        
        {/* SMTP Form Card */}
        <div className="card settings-card">
          <h2 style={{ fontFamily: 'var(--font-display)', fontWeight: 700, fontSize: '1.5rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <Mail size={24} style={{ color: 'var(--primary)' }} />
            E-posta Bildirim Ayarları (SMTP)
          </h2>

          {settingsStatus && (
            <div style={{
              padding: '0.75rem 1rem',
              background: settingsStatus.success ? 'var(--success-bg)' : 'var(--danger-bg)',
              border: `1px solid ${settingsStatus.success ? 'var(--success-border)' : 'var(--danger-border)'}`,
              color: settingsStatus.success ? 'var(--success)' : 'var(--danger)',
              borderRadius: 'var(--radius-sm)',
              fontSize: '0.85rem',
              display: 'flex',
              alignItems: 'center',
              gap: '0.5rem'
            }}>
              {settingsStatus.success ? <CheckCircle size={16} /> : <AlertCircle size={16} />}
              {settingsStatus.message}
            </div>
          )}

          <form onSubmit={handleSave} style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
            {/* Active Email Notifications Toggle */}
            <div className="switch-group">
              <div className="switch-label-wrapper">
                <span className="switch-title">E-posta Bildirimlerini Etkinleştir</span>
                <span className="switch-desc">Sigortası biten araçlar için otomatik günlük uyarı e-postaları gönderilir.</span>
              </div>
              <input
                type="checkbox"
                className="switch-input"
                checked={enableEmails}
                onChange={(e) => setEnableEmails(e.target.checked)}
              />
            </div>

            <div className="form-grid">
              <div className="form-group">
                <label className="form-label">SMTP Sunucusu (Host)</label>
                <input
                  type="text"
                  placeholder="smtp.gmail.com"
                  className="form-control"
                  value={smtpHost}
                  onChange={(e) => setSmtpHost(e.target.value)}
                  required={enableEmails}
                />
              </div>

              <div className="form-group">
                <label className="form-label">SMTP Portu</label>
                <input
                  type="number"
                  placeholder="587"
                  className="form-control"
                  value={smtpPort}
                  onChange={(e) => setSmtpPort(Number(e.target.value))}
                  required={enableEmails}
                />
              </div>

              <div className="form-group">
                <label className="form-label">SMTP Kullanıcı Adı (E-posta)</label>
                <input
                  type="email"
                  placeholder="ornek@gmail.com"
                  className="form-control"
                  value={smtpUser}
                  onChange={(e) => setSmtpUser(e.target.value)}
                  required={enableEmails}
                />
              </div>

              <div className="form-group">
                <label className="form-label">SMTP Şifresi (veya Uygulama Şifresi)</label>
                <input
                  type="password"
                  placeholder="••••••••••••••••"
                  className="form-control"
                  value={smtpPass}
                  onChange={(e) => setSmtpPass(e.target.value)}
                  required={enableEmails}
                />
              </div>

              <div className="form-group">
                <label className="form-label">Gönderen Adı</label>
                <input
                  type="text"
                  placeholder="Sigorta Takip Otomasyonu"
                  className="form-control"
                  value={senderName}
                  onChange={(e) => setSenderName(e.target.value)}
                />
              </div>

              <div className="form-group">
                <label className="form-label">Gönderen E-posta Adresi</label>
                <input
                  type="email"
                  placeholder="ornek@gmail.com"
                  className="form-control"
                  value={senderEmail}
                  onChange={(e) => setSenderEmail(e.target.value)}
                  required={enableEmails}
                />
              </div>

              <div className="form-group form-grid-full">
                <label className="form-label">Bildirimlerin Gönderileceği Alıcı E-posta</label>
                <input
                  type="email"
                  placeholder="hedef_posta@gmail.com"
                  className="form-control"
                  value={receiverEmail}
                  onChange={(e) => setReceiverEmail(e.target.value)}
                  required={enableEmails}
                />
              </div>
            </div>

            <div style={{ display: 'flex', gap: '0.75rem', marginTop: '0.5rem', flexWrap: 'wrap' }}>
              <button type="submit" className="btn-primary" disabled={saving}>
                <Save size={16} />
                {saving ? 'Kaydediliyor...' : 'Ayarları Kaydet'}
              </button>
              
              <button
                type="button"
                className="btn-secondary"
                onClick={handleTestEmail}
                disabled={testing || !smtpHost || !smtpUser || !receiverEmail}
              >
                {testing ? <RefreshCw size={16} className="spin" style={{ animation: 'pulse-text 1s infinite' }} /> : <RefreshCw size={16} />}
                Test E-postası Gönder
              </button>
            </div>
          </form>

          {testResult && (
            <div style={{
              padding: '1rem',
              background: testResult.success ? 'rgba(92, 138, 94, 0.06)' : 'rgba(196, 90, 74, 0.06)',
              border: `1px solid ${testResult.success ? 'var(--success-border)' : 'var(--danger-border)'}`,
              borderRadius: 'var(--radius-sm)',
              fontSize: '0.85rem',
              color: testResult.success ? 'var(--success)' : 'var(--danger)'
            }}>
              <h4 style={{ fontWeight: 600, marginBottom: '0.25rem', display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
                {testResult.success ? <CheckCircle size={16} /> : <AlertCircle size={16} />}
                {testResult.success ? 'Bağlantı Başarılı' : 'Bağlantı Hatası'}
              </h4>
              <p>{testResult.message}</p>
            </div>
          )}
        </div>

        {/* User Management Panel (Add/Remove & Change Password) */}
        <div className="card settings-card">
          <h2 style={{ fontFamily: 'var(--font-display)', fontWeight: 700, fontSize: '1.5rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <Key size={24} style={{ color: 'var(--primary)' }} />
            Yönetici ve Şifre Yönetimi
          </h2>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '2rem' }}>
            
            {/* Change Password Form */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
              <h3 style={{ fontSize: '1.1rem', fontWeight: 700, borderBottom: '1px solid var(--border-color)', paddingBottom: '0.5rem' }}>Şifre Değiştir</h3>
              
              {passError && <div style={{ padding: '0.5rem', background: 'var(--danger-bg)', color: 'var(--danger)', borderRadius: '4px', fontSize: '0.8rem' }}>{passError}</div>}
              {passSuccess && <div style={{ padding: '0.5rem', background: 'var(--success-bg)', color: 'var(--success)', borderRadius: '4px', fontSize: '0.8rem' }}>{passSuccess}</div>}

              <form onSubmit={handleChangePassSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                <div className="form-group">
                  <label className="form-label">Eski Şifre</label>
                  <input
                    type="password"
                    className="form-control"
                    value={oldPassword}
                    onChange={(e) => setOldPassword(e.target.value)}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Yeni Şifre</label>
                  <input
                    type="password"
                    className="form-control"
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Yeni Şifre (Tekrar)</label>
                  <input
                    type="password"
                    className="form-control"
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    required
                  />
                </div>
                <button type="submit" className="btn-primary" disabled={changingPass} style={{ marginTop: '0.5rem' }}>
                  Şifreyi Güncelle
                </button>
              </form>
            </div>

            {/* User Management */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
              <h3 style={{ fontSize: '1.1rem', fontWeight: 700, borderBottom: '1px solid var(--border-color)', paddingBottom: '0.5rem' }}>Yeni Kullanıcı Ekle</h3>

              {userActionError && <div style={{ padding: '0.5rem', background: 'var(--danger-bg)', color: 'var(--danger)', borderRadius: '4px', fontSize: '0.8rem' }}>{userActionError}</div>}
              {userActionSuccess && <div style={{ padding: '0.5rem', background: 'var(--success-bg)', color: 'var(--success)', borderRadius: '4px', fontSize: '0.8rem' }}>{userActionSuccess}</div>}

              <form onSubmit={handleAddUser} style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                <div className="form-group">
                  <label className="form-label">E-posta</label>
                  <input
                    type="email"
                    placeholder="yeni_kullanici@acente.com"
                    className="form-control"
                    value={newAdminEmail}
                    onChange={(e) => setNewAdminEmail(e.target.value)}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Şifre</label>
                  <input
                    type="password"
                    placeholder="••••••••"
                    className="form-control"
                    value={newAdminPassword}
                    onChange={(e) => setNewAdminPassword(e.target.value)}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">Yetki</label>
                  <select
                    className="form-control select-control"
                    value={newAdminRole}
                    onChange={(e) => setNewAdminRole(e.target.value as 'viewer' | 'superadmin')}
                  >
                    <option value="viewer">Gözlemci (sadece görüntüleme)</option>
                    <option value="superadmin">Yönetici (tam yetki)</option>
                  </select>
                </div>
                <button type="submit" className="btn-primary" disabled={addingUser} style={{ marginTop: '0.5rem' }}>
                  <UserPlus size={16} />
                  Kullanıcı Ekle
                </button>
              </form>

              <h3 style={{ fontSize: '1rem', fontWeight: 700, marginTop: '1rem', borderBottom: '1px solid var(--border-color)', paddingBottom: '0.5rem' }}>Mevcut Kullanıcılar</h3>
              <div className="user-list">
                {users.map(u => {
                  const isSelf = u.email.toLowerCase() === currentUserEmail.toLowerCase();
                  const isAdmin = u.role === 'superadmin';
                  const canManage = !isSelf && !u.isBootstrap;
                  return (
                    <div key={u.id} className="user-item">
                      <span className="user-email">
                        {u.email}
                        <span className="user-tag" style={{ background: isAdmin ? 'rgba(139, 90, 60, 0.12)' : 'rgba(139, 90, 60, 0.06)', color: isAdmin ? 'var(--primary)' : 'var(--text-secondary)' }}>
                          {isAdmin ? 'Yönetici' : 'Gözlemci'}
                        </span>
                        {isSelf && <span className="user-tag">Siz</span>}
                      </span>
                      {canManage && (
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
                          <button
                            className="btn-secondary"
                            onClick={() => handleChangeRole(u.email, isAdmin ? 'viewer' : 'superadmin')}
                            title={isAdmin ? 'Gözlemciye düşür' : 'Yönetici yap'}
                            style={{ padding: '0.3rem 0.6rem', fontSize: '0.75rem' }}
                          >
                            {isAdmin ? 'Gözlemci Yap' : 'Yönetici Yap'}
                          </button>
                          <button
                            className="icon-btn delete"
                            onClick={() => handleDeleteUser(u.email)}
                            title="Kullanıcıyı Sil"
                          >
                            <Trash2 size={16} />
                          </button>
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            </div>

          </div>
        </div>

      </div>

      {/* Database Management / Sidebar Card */}
      <div className="card" style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
        <h2 style={{ fontFamily: 'var(--font-display)', fontWeight: 700, fontSize: '1.3rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
          <Shield size={20} style={{ color: 'var(--primary)' }} />
          Veri Yönetimi
        </h2>
        
        <p style={{ fontSize: '0.85rem', color: 'var(--text-secondary)', lineHeight: '1.5' }}>
          Tüm verileriniz ve ayarlarınız yerel diskte güvenle saklanır. Her ihtimale karşı periyodik yedek alabilirsiniz.
        </p>

        <hr style={{ borderColor: 'var(--border-color)' }} />

        {/* Backup Button */}
        <div>
          <button className="btn-secondary" onClick={handleBackupDownload} style={{ width: '100%', justifyContent: 'center' }}>
            <Download size={16} />
            Yedek İndir (.json)
          </button>
          <p style={{ fontSize: '0.7rem', color: 'var(--text-muted)', marginTop: '0.4rem', lineHeight: 1.4 }}>
            Güvenlik nedeniyle SMTP şifresi yedeğe dahil edilmez. Geri yükleme sonrası mevcut SMTP şifreniz korunur.
          </p>
        </div>

        {/* Restore Button */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
          <label className="btn-secondary" style={{ width: '100%', justifyContent: 'center', cursor: 'pointer' }}>
            <Upload size={16} />
            Yedekten Geri Yükle
            <input
              type="file"
              accept=".json"
              style={{ display: 'none' }}
              onChange={handleBackupUpload}
            />
          </label>
        </div>

        {restoreError && (
          <div style={{ padding: '0.5rem 0.75rem', background: 'var(--danger-bg)', border: '1px solid var(--danger-border)', color: 'var(--danger)', borderRadius: 'var(--radius-sm)', fontSize: '0.8rem' }}>
            {restoreError}
          </div>
        )}

        {restoreSuccess && (
          <div style={{ padding: '0.5rem 0.75rem', background: 'var(--success-bg)', border: '1px solid var(--success-border)', color: 'var(--success)', borderRadius: 'var(--radius-sm)', fontSize: '0.8rem' }}>
            {restoreSuccess}
          </div>
        )}

        <hr style={{ borderColor: 'var(--border-color)' }} />

        {/* Excel Export Button */}
        <div>
          <button className="btn-secondary" onClick={handleExcelExport} style={{ width: '100%', justifyContent: 'center', background: 'rgba(92, 138, 94, 0.05)', color: 'var(--success)', borderColor: 'var(--success-border)' }}>
            <Download size={16} />
            Excel İndir (.xlsx)
          </button>
        </div>

        {/* Excel Import Button */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
          <label className="btn-secondary" style={{ width: '100%', justifyContent: 'center', cursor: 'pointer', background: 'rgba(92, 138, 94, 0.05)', color: 'var(--success)', borderColor: 'var(--success-border)' }}>
            <Upload size={16} />
            Excel'den Araç Yükle
            <input
              type="file"
              accept=".xlsx, .xls"
              style={{ display: 'none' }}
              onChange={handleExcelImport}
            />
          </label>
        </div>

        {excelError && (
          <div style={{ padding: '0.5rem 0.75rem', background: 'var(--danger-bg)', border: '1px solid var(--danger-border)', color: 'var(--danger)', borderRadius: 'var(--radius-sm)', fontSize: '0.8rem' }}>
            {excelError}
          </div>
        )}

        {excelSuccess && (
          <div style={{ padding: '0.5rem 0.75rem', background: 'var(--success-bg)', border: '1px solid var(--success-border)', color: 'var(--success)', borderRadius: 'var(--radius-sm)', fontSize: '0.8rem' }}>
            {excelSuccess}
          </div>
        )}
      </div>
    </div>
  );
};
