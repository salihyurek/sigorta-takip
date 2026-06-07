import { useState, useEffect, useRef, useCallback, Component, type ReactNode, type ErrorInfo } from 'react';
import type { BackupData, Bus, SMTPConfig } from './types';
import { Dashboard } from './components/Dashboard';
import { BusTable } from './components/BusTable';
import { BusForm } from './components/BusForm';
import { Settings } from './components/Settings';
import { Login } from './components/Login';
import { ResetPassword } from './components/ResetPassword';
import { ShieldCheck, Plus, Settings as SettingsIcon, LayoutDashboard, Bus as BusIcon, X, LogOut } from 'lucide-react';

interface Toast {
  id: string;
  type: 'success' | 'error' | 'info';
  message: string;
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback;
}

class ErrorBoundary extends Component<{ children: ReactNode }, { hasError: boolean }> {
  constructor(props: { children: ReactNode }) {
    super(props);
    this.state = { hasError: false };
  }

  static getDerivedStateFromError() {
    return { hasError: true };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('Application Error:', error, errorInfo);
  }

  render() {
    if (this.state.hasError) {
      return (
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100vh', flexDirection: 'column', gap: '1rem', background: 'var(--bg-primary)', color: 'var(--text-primary)' }}>
          <ShieldCheck size={48} style={{ color: 'var(--danger)' }} />
          <h2>Uygulama Hatası</h2>
          <p style={{ color: 'var(--text-secondary)' }}>Beklenmeyen bir hata oluştu.</p>
          <button className="btn-primary" onClick={() => window.location.reload()}>Sayfayı Yenile</button>
        </div>
      );
    }
    return this.props.children;
  }
}

export default function App() {
  // Authentication State
  const [token, setToken] = useState<string | null>(
    localStorage.getItem('token') || sessionStorage.getItem('token')
  );
  const [userEmail, setUserEmail] = useState<string | null>(
    localStorage.getItem('email') || sessionStorage.getItem('email')
  );
  const [role, setRole] = useState<string | null>(
    localStorage.getItem('role') || sessionStorage.getItem('role')
  );

  // Forgot Password Reset Token in URL
  const [resetToken, setResetToken] = useState<string | null>(() => {
    const urlParams = new URLSearchParams(window.location.search);
    return urlParams.get('resetToken');
  });

  const [buses, setBuses] = useState<Bus[]>([]);
  const [settings, setSettings] = useState<SMTPConfig | null>(null);
  const [activeTab, setActiveTab] = useState<'dashboard' | 'buses' | 'settings'>('dashboard');
  
  // Modal State
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingBus, setEditingBus] = useState<Bus | null>(null);

  // Toasts State
  const [toasts, setToasts] = useState<Toast[]>([]);
  const toastCounterRef = useRef(0);

  const removeToast = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  // Add Toast Notification Helper
  const addToast = useCallback((type: 'success' | 'error' | 'info', message: string) => {
    toastCounterRef.current += 1;
    const id = `toast-${toastCounterRef.current}`;
    setToasts((prev) => [...prev, { id, type, message }]);
    
    // Auto remove after 5 seconds
    setTimeout(() => {
      removeToast(id);
    }, 5000);
  }, [removeToast]);

  // Auth logout helper for local cleanup
  const handleLogoutClient = useCallback(() => {
    localStorage.removeItem('token');
    localStorage.removeItem('email');
    localStorage.removeItem('role');
    sessionStorage.removeItem('token');
    sessionStorage.removeItem('email');
    sessionStorage.removeItem('role');
    setToken(null);
    setUserEmail(null);
    setRole(null);
    setActiveTab('dashboard');
  }, []);

  // Helper to handle API responses and automatically logout on 401
  const handleApiResponse = useCallback(async (res: Response) => {
    if (res.status === 401) {
      handleLogoutClient();
      addToast('error', 'Oturumunuz sonlandırıldı. Lütfen tekrar giriş yapın.');
      throw new Error('Unauthorized');
    }
    return res;
  }, [addToast, handleLogoutClient]);

  // Safe JSON parser helper
  const safeJsonParse = useCallback(async (res: Response) => {
    try {
      return await res.json();
    } catch {
      return { error: 'Sunucudan geçersiz yanıt alındı.' };
    }
  }, []);

  // Fetch initial data (only when authenticated)
  const fetchBuses = useCallback(async () => {
    if (!token) return;
    try {
      const res = await fetch('/api/buses', {
        headers: { 'Authorization': `Bearer ${token}` }
      });
      await handleApiResponse(res);
      const data = await safeJsonParse(res);
      if (!res.ok) throw new Error(data.error || 'Araç listesi yüklenemedi');
      setBuses(data);
    } catch (err: unknown) {
      const message = getErrorMessage(err, 'Araçlar yüklenirken hata oluştu.');
      if (message !== 'Unauthorized') {
        addToast('error', message);
      }
    }
  }, [addToast, handleApiResponse, safeJsonParse, token]);

  const fetchSettings = useCallback(async () => {
    // SMTP settings are superadmin-only on the server; viewers never request them.
    if (!token || role !== 'superadmin') return;
    try {
      const res = await fetch('/api/settings', {
        headers: { 'Authorization': `Bearer ${token}` }
      });
      await handleApiResponse(res);
      const data = await safeJsonParse(res);
      if (!res.ok) throw new Error(data.error || 'Ayarlar yüklenemedi');
      setSettings(data);
    } catch (err: unknown) {
      const message = getErrorMessage(err, 'Ayarlar yüklenirken hata oluştu.');
      if (message !== 'Unauthorized') {
        addToast('error', message);
      }
    }
  }, [addToast, handleApiResponse, safeJsonParse, token, role]);

  useEffect(() => {
    if (token) {
      fetchBuses();
      fetchSettings();
    }
  }, [token, fetchBuses, fetchSettings]);

  // Verify role from server on mount
  useEffect(() => {
    if (token) {
      fetch('/api/auth/me', {
        headers: { 'Authorization': `Bearer ${token}` }
      })
      .then(res => {
        if (res.status === 401) {
          handleLogoutClient();
          return null;
        }
        return res.json();
      })
      .then(data => {
        if (data && data.role) {
          setRole(data.role);
          // Sync storage
          const storage = localStorage.getItem('token') ? localStorage : sessionStorage;
          storage.setItem('role', data.role);
        }
      })
      .catch(() => {});
    }
  }, [token, handleLogoutClient]);

  // Cross-tab session sync
  useEffect(() => {
    const handleStorageChange = (e: StorageEvent) => {
      if (e.key === 'token' && !e.newValue) {
        handleLogoutClient();
      }
    };
    window.addEventListener('storage', handleStorageChange);
    return () => window.removeEventListener('storage', handleStorageChange);
  }, [handleLogoutClient]);

  // Login handler
  const handleLogin = async (email: string, password: string, remember: boolean) => {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password })
    });
    
    const data = await safeJsonParse(res);
    if (!res.ok) {
      throw new Error(data.error || 'Giriş başarısız.');
    }

    if (remember) {
      localStorage.setItem('token', data.token);
      localStorage.setItem('email', data.email);
      localStorage.setItem('role', data.role);
    } else {
      sessionStorage.setItem('token', data.token);
      sessionStorage.setItem('email', data.email);
      sessionStorage.setItem('role', data.role);
    }

    setToken(data.token);
    setUserEmail(data.email);
    setRole(data.role);
    addToast('success', 'Giriş başarılı! Yönetici paneline yönlendirildiniz.');
  };

  // Logout handler
  const handleLogout = async () => {
    if (token) {
      try {
        await fetch('/api/auth/logout', {
          method: 'POST',
          headers: { 'Authorization': `Bearer ${token}` }
        });
      } catch (err) {
        console.error('Logout error on server:', err);
      }
    }
    handleLogoutClient();
    addToast('info', 'Oturum kapatıldı.');
  };

  // Change Password handler
  const handleChangePassword = async (oldPass: string, newPass: string) => {
    if (!token) return;
    const res = await fetch('/api/auth/change-password', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`
      },
      body: JSON.stringify({ oldPassword: oldPass, newPassword: newPass })
    });

    await handleApiResponse(res);
    const data = await safeJsonParse(res);
    if (!res.ok) throw new Error(data.error || 'Şifre değiştirilemedi.');

    addToast('success', 'Şifreniz başarıyla değiştirildi.');
  };

  // Save Bus (Create or Update) - Super Admin Only
  const handleSaveBus = async (busData: Omit<Bus, 'id'> & { id?: string }) => {
    if (!token) return;
    const isEdit = !!busData.id;
    const url = isEdit ? `/api/buses/${busData.id}` : '/api/buses';
    const method = isEdit ? 'PUT' : 'POST';

    const res = await fetch(url, {
      method,
      headers: { 
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`
      },
      body: JSON.stringify(busData)
    });

    await handleApiResponse(res);
    const data = await safeJsonParse(res);

    if (!res.ok) {
      throw new Error(data.error || 'Kaydetme işlemi başarısız.');
    }

    addToast('success', isEdit ? `${busData.plate} plakalı araç güncellendi.` : `${busData.plate} plakalı araç eklendi.`);
    fetchBuses();
  };

  // Delete Bus - Super Admin Only
  const handleDeleteBus = async (id: string) => {
    if (!token) return;
    try {
      const res = await fetch(`/api/buses/${id}`, {
        method: 'DELETE',
        headers: { 'Authorization': `Bearer ${token}` }
      });
      await handleApiResponse(res);
      const data = await safeJsonParse(res);
      if (!res.ok) throw new Error(data.error || 'Silme işlemi başarısız.');

      addToast('success', 'Araç veritabanından silindi.');
      fetchBuses();
    } catch (err: unknown) {
      const message = getErrorMessage(err, 'Silme işlemi başarısız.');
      if (message !== 'Unauthorized') {
        addToast('error', message);
      }
    }
  };

  // Save SMTP Settings - Super Admin Only
  const handleSaveSettings = async (newSettings: SMTPConfig) => {
    if (!token) return;
    const res = await fetch('/api/settings', {
      method: 'POST',
      headers: { 
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`
      },
      body: JSON.stringify(newSettings)
    });

    await handleApiResponse(res);
    const data = await safeJsonParse(res);
    if (!res.ok) throw new Error(data.error || 'Ayarlar kaydedilemedi.');

    setSettings(data.settings);
    addToast('success', 'E-posta SMTP ayarları kaydedildi.');
  };

  // Test SMTP Email Settings - Super Admin Only
  const handleTestEmail = async (testSettings: SMTPConfig) => {
    if (!token) return;
    const res = await fetch('/api/settings/test-email', {
      method: 'POST',
      headers: { 
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`
      },
      body: JSON.stringify(testSettings)
    });

    await handleApiResponse(res);
    const data = await safeJsonParse(res);
    if (!res.ok) throw new Error(data.error || 'E-posta testi başarısız.');

    addToast('success', 'Test e-postası başarıyla gönderildi!');
  };

  // Restore database - Super Admin Only
  const handleRestoreDb = async (backupData: BackupData) => {
    if (!token) return;
    try {
      const res = await fetch('/api/restore', {
        method: 'POST',
        headers: { 
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${token}`
        },
        body: JSON.stringify(backupData)
      });

      await handleApiResponse(res);
      const data = await safeJsonParse(res);
      if (!res.ok) throw new Error(data.error || 'Geri yükleme başarısız.');

      addToast('success', 'Tüm yedek verileri başarıyla yüklendi.');
      fetchBuses();
      fetchSettings();
    } catch (err: unknown) {
      const message = getErrorMessage(err, 'Geri yükleme başarısız.');
      if (message !== 'Unauthorized') {
        addToast('error', message);
      }
      throw err;
    }
  };

  // Excel Import - Super Admin Only
  const handleImportExcel = async (importedBuses: Omit<Bus, 'id'>[]) => {
    if (!token) return;
    try {
      const res = await fetch('/api/buses/bulk-import', {
        method: 'POST',
        headers: { 
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${token}`
        },
        body: JSON.stringify(importedBuses)
      });

      await handleApiResponse(res);
      const data = await safeJsonParse(res);
      if (!res.ok) throw new Error(data.error || 'İçe aktarma işlemi başarısız oldu.');

      addToast('success', `Excel verileri yüklendi: ${data.importedCount} yeni araç eklendi, ${data.updatedCount} araç güncellendi.`);
      fetchBuses();
    } catch (err: unknown) {
      const message = getErrorMessage(err, 'İçe aktarma başarısız.');
      if (message !== 'Unauthorized') {
        addToast('error', message);
      }
      throw err;
    }
  };

  // Trigger Expiry Check manually
  const handleCheckExpiries = async () => {
    if (!token) return;
    try {
      addToast('info', 'E-posta bildirim kontrolü tetiklendi...');
      const res = await fetch('/api/check-expiries', { 
        method: 'POST',
        headers: { 'Authorization': `Bearer ${token}` }
      });
      await handleApiResponse(res);
      const data = await safeJsonParse(res);
      
      if (!res.ok) throw new Error(data.error || 'Kontrol işlemi başarısız.');
      
      if (data.sentCount > 0) {
        addToast('success', `Kontrol tamamlandı. ${data.sentCount} adet uyarı e-postası gönderildi!`);
      } else {
        addToast('info', 'Kontrol tamamlandı. Gönderilecek yeni bir uyarı e-postası bulunmuyor.');
      }
      fetchBuses();
    } catch (err: unknown) {
      const message = getErrorMessage(err, 'Kontrol işlemi başarısız.');
      if (message !== 'Unauthorized') {
        addToast('error', message);
      }
    }
  };

  const openAddModal = () => {
    if (role !== 'superadmin') return;
    setEditingBus(null);
    setIsFormOpen(true);
  };

  const openEditModal = (bus: Bus) => {
    if (role !== 'superadmin') return;
    setEditingBus(bus);
    setIsFormOpen(true);
  };

  const handleResetComplete = () => {
    // Clear token query parameter from URL
    window.history.replaceState({}, document.title, window.location.pathname);
    setResetToken(null);
    addToast('success', 'Şifreniz başarıyla güncellendi! Yeni şifrenizle giriş yapabilirsiniz.');
  };

  // Render Password Reset View if URL has reset token
  if (resetToken) {
    return (
      <>
        <ResetPassword token={resetToken} onResetComplete={handleResetComplete} />
        {/* Floating Toast Notifications */}
        <div className="toast-container">
          {toasts.map((toast) => (
            <div key={toast.id} className={`toast ${toast.type}`}>
              <span className="toast-message">{toast.message}</span>
              <button className="toast-close" onClick={() => removeToast(toast.id)}>
                <X size={16} />
              </button>
            </div>
          ))}
        </div>
      </>
    );
  }

  // Conditional Rendering based on Authentication
  if (!token) {
    return (
      <>
        <Login onLogin={handleLogin} />
        {/* Floating Toast Notifications */}
        <div className="toast-container">
          {toasts.map((toast) => (
            <div key={toast.id} className={`toast ${toast.type}`}>
              <span className="toast-message">{toast.message}</span>
              <button className="toast-close" onClick={() => removeToast(toast.id)}>
                <X size={16} />
              </button>
            </div>
          ))}
        </div>
      </>
    );
  }

  const isSuperAdmin = role === 'superadmin';

  return (
    <ErrorBoundary>
    <div className="app-container">
      {/* Navigation */}
      <nav className="navbar">
        <div className="brand-container">
          <ShieldCheck size={32} className="brand-icon" />
          <div style={{ display: 'flex', flexDirection: 'column' }}>
            <span className="brand-name" style={{ lineHeight: '1.2' }}>Anadolu Sigorta</span>
            <span style={{ fontSize: '0.75rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Turhal Tokat Acentesi - Mustafa Bulut</span>
          </div>
        </div>
        
        <div className="nav-links">
          <button
            className={`nav-btn ${activeTab === 'dashboard' ? 'active' : ''}`}
            onClick={() => setActiveTab('dashboard')}
          >
            <LayoutDashboard size={18} />
            Genel Bakış
          </button>
          
          <button
            className={`nav-btn ${activeTab === 'buses' ? 'active' : ''}`}
            onClick={() => setActiveTab('buses')}
          >
            <BusIcon size={18} />
            Araç Listesi
          </button>
          
          <button
            className={`nav-btn ${activeTab === 'settings' ? 'active' : ''}`}
            onClick={() => setActiveTab('settings')}
          >
            <SettingsIcon size={18} />
            Ayarlar
          </button>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
          {isSuperAdmin && (
            <button className="btn-primary" onClick={openAddModal}>
              <Plus size={18} />
              Yeni Araç Ekle
            </button>
          )}
          
          {isSuperAdmin && (
            <div style={{ width: '1px', height: '24px', backgroundColor: 'var(--border-color)' }}></div>
          )}
          
          <span style={{ fontSize: '0.85rem', color: 'var(--text-secondary)', fontWeight: 500 }}>
            {userEmail} ({isSuperAdmin ? 'Yönetici' : 'Gözlemci'})
          </span>
          
          <button className="btn-secondary" onClick={handleLogout} title="Oturumu Kapat" style={{ padding: '0.5rem 0.75rem' }}>
            <LogOut size={16} />
          </button>
        </div>
      </nav>

      {/* Main Content Area */}
      <main className="main-content">
        {activeTab === 'dashboard' && (
          <Dashboard
            buses={buses}
            onEditBus={openEditModal}
            onCheckExpiries={handleCheckExpiries}
            isSuperAdmin={isSuperAdmin}
          />
        )}
        
        {activeTab === 'buses' && (
          <BusTable
            buses={buses}
            onEditBus={openEditModal}
            onDeleteBus={handleDeleteBus}
            isSuperAdmin={isSuperAdmin}
          />
        )}
        
        {/* Superadmins wait for settings to load; viewers only need the password
            form, so they see the tab immediately (settings stays null for them). */}
        {activeTab === 'settings' && (isSuperAdmin ? settings !== null : true) && (
          <Settings
            settings={settings}
            onSaveSettings={handleSaveSettings}
            onTestEmail={handleTestEmail}
            onRestoreDb={handleRestoreDb}
            onImportExcel={handleImportExcel}
            busesData={buses}
            currentUserEmail={userEmail || ''}
            currentUserRole={role || ''}
            onChangePassword={handleChangePassword}
          />
        )}
      </main>

      {/* Footer */}
      <footer style={{ textAlign: 'center', padding: '1.5rem', fontSize: '0.8rem', color: 'var(--text-muted)', borderTop: '1px solid var(--border-color)', background: 'rgba(250, 247, 243, 0.95)' }}>
        Anadolu Sigorta Tokat Turhal Acentesi &copy; {new Date().getFullYear()} • Mustafa Bulut • Otobüs Sigorta Takip Otomasyon Paneli
      </footer>

      {/* Add / Edit Form Modal */}
      {isFormOpen && (
        <BusForm
          bus={editingBus}
          onSave={handleSaveBus}
          onClose={() => setIsFormOpen(false)}
        />
      )}

      {/* Floating Toast Notifications */}
      <div className="toast-container">
        {toasts.map((toast) => (
          <div key={toast.id} className={`toast ${toast.type}`}>
            <span className="toast-message">{toast.message}</span>
            <button className="toast-close" onClick={() => removeToast(toast.id)}>
              <X size={16} />
            </button>
          </div>
        ))}
      </div>
    </div>
    </ErrorBoundary>
  );
}
