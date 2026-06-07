import { useState, type FormEvent } from 'react';
import { ShieldCheck, Mail, Lock, RefreshCw, AlertCircle, CheckCircle, ArrowLeft } from 'lucide-react';

interface LoginProps {
  onLogin: (email: string, password: string, remember: boolean) => Promise<void>;
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback;
}

export const Login = ({ onLogin }: LoginProps) => {
  const [view, setView] = useState<'login' | 'forgot'>('login');
  
  // Login fields
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [remember, setRemember] = useState(true);
  
  // Forgot password fields
  const [forgotEmail, setForgotEmail] = useState('');
  const [forgotSuccess, setForgotSuccess] = useState<string | null>(null);
  
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    
    if (!email || !password) {
      setError('Lütfen e-posta adresinizi ve şifrenizi girin.');
      return;
    }

    try {
      setLoading(true);
      await onLogin(email.trim(), password, remember);
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Giriş yapılamadı. E-posta adresi veya şifre hatalı olabilir.'));
    } finally {
      setLoading(false);
    }
  };

  const handleForgotSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setForgotSuccess(null);

    if (!forgotEmail) {
      setError('Lütfen e-posta adresinizi girin.');
      return;
    }

    try {
      setLoading(true);
      const res = await fetch('/api/auth/forgot-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: forgotEmail.trim() })
      });

      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Talep gönderilemedi.');

      setForgotSuccess(data.message || 'Sıfırlama bağlantısı e-postanıza başarıyla gönderildi.');
      setForgotEmail('');
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Şifre sıfırlama talebi gönderilemedi.'));
    } finally {
      setLoading(false);
    }
  };

  const toggleView = (targetView: 'login' | 'forgot') => {
    setError(null);
    setForgotSuccess(null);
    setView(targetView);
  };

  return (
    <div className="login-container">
      <div className="login-card">
        <div className="login-header">
          <div className="login-icon-wrapper">
            <ShieldCheck size={32} />
          </div>
          <h2 className="login-title">SİGORTA TAKİP</h2>
          <p className="login-subtitle">
            {view === 'login' ? 'Devam etmek için yönetici girişi yapın' : 'Şifre Sıfırlama Talebi'}
          </p>
        </div>

        {error && (
          <div style={{
            padding: '0.75rem 1rem',
            background: 'var(--danger-bg)',
            border: '1px solid var(--danger-border)',
            color: 'var(--danger)',
            borderRadius: 'var(--radius-sm)',
            fontSize: '0.85rem',
            lineHeight: '1.4',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem'
          }}>
            <AlertCircle size={16} style={{ flexShrink: 0 }} />
            <span>{error}</span>
          </div>
        )}

        {forgotSuccess && (
          <div style={{
            padding: '0.75rem 1rem',
            background: 'var(--success-bg)',
            border: '1px solid var(--success-border)',
            color: 'var(--success)',
            borderRadius: 'var(--radius-sm)',
            fontSize: '0.85rem',
            lineHeight: '1.4',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem'
          }}>
            <CheckCircle size={16} style={{ flexShrink: 0 }} />
            <span>{forgotSuccess}</span>
          </div>
        )}

        {view === 'login' ? (
          /* Login Form */
          <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
            <div className="form-group">
              <label className="form-label">
                <Mail size={12} /> E-posta Adresi
              </label>
              <input
                type="email"
                placeholder="yonetici@acente.com"
                className="form-control"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                disabled={loading}
                required
              />
            </div>

            <div className="form-group">
              <label className="form-label">
                <Lock size={12} /> Şifre
              </label>
              <input
                type="password"
                placeholder="••••••••"
                className="form-control"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                disabled={loading}
                required
              />
            </div>

            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <div className="switch-group" style={{ padding: '0.4rem 0.6rem', border: 'none', background: 'transparent' }}>
                <input
                  type="checkbox"
                  className="switch-input"
                  checked={remember}
                  onChange={(e) => setRemember(e.target.checked)}
                  disabled={loading}
                  style={{ width: '30px', height: '16px', marginRight: '0.5rem' }}
                />
                <span className="switch-title" style={{ fontSize: '0.8rem', color: 'var(--text-secondary)' }}>Beni Hatırla</span>
              </div>

              <button
                type="button"
                className="nav-btn"
                onClick={() => toggleView('forgot')}
                style={{ fontSize: '0.8rem', textDecoration: 'underline', padding: 0, height: 'auto', border: 'none' }}
                disabled={loading}
              >
                Şifremi Unuttum
              </button>
            </div>

            <button type="submit" className="btn-primary" disabled={loading} style={{ justifyContent: 'center', marginTop: '0.5rem', padding: '0.75rem' }}>
              {loading ? (
                <>
                  <RefreshCw size={18} className="spin" style={{ animation: 'pulse-text 1s infinite' }} />
                  Giriş Yapılıyor...
                </>
              ) : (
                'Giriş Yap'
              )}
            </button>
          </form>
        ) : (
          /* Forgot Password Form */
          <form onSubmit={handleForgotSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
            <div className="form-group">
              <label className="form-label">
                <Mail size={12} /> Kayıtlı E-posta Adresi
              </label>
              <input
                type="email"
                placeholder="yonetici@acente.com"
                className="form-control"
                value={forgotEmail}
                onChange={(e) => setForgotEmail(e.target.value)}
                disabled={loading}
                required
              />
            </div>

            <button type="submit" className="btn-primary" disabled={loading} style={{ justifyContent: 'center', marginTop: '0.5rem', padding: '0.75rem' }}>
              {loading ? (
                <>
                  <RefreshCw size={18} className="spin" style={{ animation: 'pulse-text 1s infinite' }} />
                  Bağlantı Gönderiliyor...
                </>
              ) : (
                'Sıfırlama Bağlantısı Gönder'
              )}
            </button>

            <button
              type="button"
              className="btn-secondary"
              onClick={() => toggleView('login')}
              disabled={loading}
              style={{ justifyContent: 'center', gap: '0.5rem' }}
            >
              <ArrowLeft size={16} />
              Giriş Sayfasına Dön
            </button>
          </form>
        )}
        
        <div style={{ textAlign: 'center', fontSize: '0.75rem', color: 'var(--text-muted)' }}>
          Sigorta Takip Otomasyon © 2026
        </div>
      </div>
    </div>
  );
};
