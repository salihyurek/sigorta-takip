import { useState, type FormEvent } from 'react';
import { ShieldCheck, Lock, RefreshCw, CheckCircle, AlertCircle } from 'lucide-react';

interface ResetPasswordProps {
  token: string;
  onResetComplete: () => void;
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback;
}

const safeJson = async (res: Response) => {
  try { return await res.json(); } catch { return null; }
};

export const ResetPassword = ({ token, onResetComplete }: ResetPasswordProps) => {
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setSuccess(null);

    if (!password || !confirmPassword) {
      setError('Lütfen tüm alanları doldurun.');
      return;
    }

    if (password !== confirmPassword) {
      setError('Girdiğiniz şifreler uyuşmuyor.');
      return;
    }

    if (password.length < 8) {
      setError('Şifreniz en az 8 karakter uzunluğunda olmalıdır.');
      return;
    }

    if (!/[A-Z]/.test(password) || !/[a-z]/.test(password) || !/[0-9]/.test(password)) {
      setError('Şifre en az bir büyük harf, bir küçük harf ve bir rakam içermelidir.');
      return;
    }

    try {
      setLoading(true);
      const res = await fetch('/api/auth/reset-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token, password })
      });

      const data = await safeJson(res);
      if (!res.ok) throw new Error(data?.error || 'Şifre sıfırlama işlemi başarısız.');

      setSuccess('Şifreniz başarıyla güncellendi! Giriş sayfasına yönlendiriliyorsunuz...');
      
      // Redirect back after 3 seconds
      setTimeout(() => {
        onResetComplete();
      }, 3000);
      
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Şifre sıfırlanamadı.'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-container">
      <div className="login-card">
        <div className="login-header">
          <div className="login-icon-wrapper" style={{ background: 'rgba(139, 90, 60, 0.1)', color: 'var(--primary)' }}>
            <ShieldCheck size={32} />
          </div>
          <h2 className="login-title" style={{ background: 'linear-gradient(135deg, #6b4229 0%, #a67c5b 100%)', WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent' }}>
            Yeni Şifre Belirle
          </h2>
          <p className="login-subtitle">Hesabınız için yeni bir şifre tanımlayın</p>
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

        {success && (
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
            <span>{success}</span>
          </div>
        )}

        {!success && (
          <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
            <div className="form-group">
              <label className="form-label">
                <Lock size={12} /> Yeni Şifre
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

            <div className="form-group">
              <label className="form-label">
                <Lock size={12} /> Yeni Şifre (Tekrar)
              </label>
              <input
                type="password"
                placeholder="••••••••"
                className="form-control"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                disabled={loading}
                required
              />
            </div>

            <button type="submit" className="btn-primary" disabled={loading} style={{ justifyContent: 'center', marginTop: '0.5rem', padding: '0.75rem', background: 'linear-gradient(135deg, #8b5a3c 0%, #6b4229 100%)' }}>
              {loading ? (
                <>
                  <RefreshCw size={18} className="spin" style={{ animation: 'pulse-text 1s infinite' }} />
                  Kaydediliyor...
                </>
              ) : (
                'Şifreyi Kaydet'
              )}
            </button>
          </form>
        )}
      </div>
    </div>
  );
};
