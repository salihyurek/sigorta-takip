import type { Bus } from '../types';
import { getPolicyStatus, getDaysRemaining, formatDate, SOON_THRESHOLD_DAYS } from '../utils/dateHelpers';
import { ShieldAlert, AlertTriangle, CheckCircle, Bus as BusIcon, Bell } from 'lucide-react';

interface DashboardProps {
  buses: Bus[];
  onEditBus: (bus: Bus) => void;
  onCheckExpiries: () => void;
  isSuperAdmin?: boolean;
}

export const Dashboard = ({ buses, onEditBus, onCheckExpiries, isSuperAdmin = true }: DashboardProps) => {
  // Calculate stats
  const totalBuses = buses.length;
  let expiredCount = 0;
  let todayCount = 0;
  let soonCount = 0;
  let activeCount = 0;

  const alerts: Array<{
    bus: Bus;
    policyKey: 'trafik' | 'kasko' | 'koltuk';
    status: 'expired' | 'today' | 'soon' | 'active';
    daysRemaining: number;
  }> = [];

  buses.forEach((bus) => {
    (['trafik', 'kasko', 'koltuk'] as const).forEach((policyKey) => {
      const policy = bus.policies[policyKey];
      if (policy) {
        const status = getPolicyStatus(policy.endDate);
        const days = getDaysRemaining(policy.endDate);

        if (status === 'expired') expiredCount++;
        else if (status === 'today') todayCount++;
        else if (status === 'soon') soonCount++;
        else activeCount++;

        if (status !== 'active') {
          alerts.push({
            bus,
            policyKey,
            status,
            daysRemaining: days
          });
        }
      }
    });
  });

  // Sort alerts by urgency: expired first, then today, then soon (ascending order of remaining days)
  alerts.sort((a, b) => a.daysRemaining - b.daysRemaining);

  const policyNames = {
    trafik: 'Zorunlu Trafik Sigortası',
    kasko: 'Kasko',
    koltuk: 'Koltuk Sigortası'
  };

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1.5rem' }}>
        <h2 style={{ fontFamily: 'var(--font-display)', fontWeight: 700, fontSize: '1.5rem' }}>
          Genel Özet
        </h2>
        {isSuperAdmin && (
          <button className="btn-secondary" onClick={onCheckExpiries} style={{ gap: '0.5rem' }}>
            <Bell size={16} />
            E-posta Uyarılarını Kontrol Et
          </button>
        )}
      </div>

      {/* Metrics Cards */}
      <div className="metrics-grid">
        <div className="card metric-card metric-primary">
          <div className="metric-icon-wrapper">
            <BusIcon size={24} />
          </div>
          <div className="metric-info">
            <span className="metric-label">Kayıtlı Araçlar</span>
            <span className="metric-value">{totalBuses}</span>
          </div>
        </div>

        <div className="card metric-card metric-expired">
          <div className="metric-icon-wrapper">
            <ShieldAlert size={24} />
          </div>
          <div className="metric-info">
            <span className="metric-label">Günü Geçmiş Poliçe</span>
            <span className="metric-value">{expiredCount}</span>
          </div>
        </div>

        <div className="card metric-card metric-danger">
          <div className="metric-icon-wrapper">
            <ShieldAlert size={24} style={{ animation: 'pulse-text 1s infinite alternate' }} />
          </div>
          <div className="metric-info">
            <span className="metric-label">Bugün Biten Poliçe</span>
            <span className="metric-value">{todayCount}</span>
          </div>
        </div>

        <div className="card metric-card metric-warning">
          <div className="metric-icon-wrapper">
            <AlertTriangle size={24} />
          </div>
          <div className="metric-info">
            <span className="metric-label">{SOON_THRESHOLD_DAYS} Gün İçinde Biten</span>
            <span className="metric-value">{soonCount}</span>
          </div>
        </div>

        <div className="card metric-card metric-success">
          <div className="metric-icon-wrapper">
            <CheckCircle size={24} />
          </div>
          <div className="metric-info">
            <span className="metric-label">Aktif / Güvenli</span>
            <span className="metric-value">{activeCount}</span>
          </div>
        </div>
      </div>

      <div className="dashboard-grid">
        {/* Urgent Alerts Panel */}
        <div className="card">
          <h3 style={{ fontFamily: 'var(--font-display)', fontWeight: 700, fontSize: '1.2rem', marginBottom: '1.25rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <AlertTriangle size={20} className="alert-item-icon-danger" />
            Acil Eylem Gerektiren Poliçeler
          </h3>

          {alerts.length === 0 ? (
            <div style={{ textAlign: 'center', padding: '3rem 1rem', color: 'var(--text-secondary)' }}>
              <CheckCircle size={48} style={{ color: 'var(--success)', marginBottom: '1rem' }} />
              <p style={{ fontWeight: 600, fontSize: '1.1rem', color: 'var(--text-primary)', marginBottom: '0.25rem' }}>Harika! Her Şey Yolunda</p>
              <p style={{ fontSize: '0.85rem' }}>Süresi yaklaşan veya geçen herhangi bir sigorta poliçesi bulunmuyor.</p>
            </div>
          ) : (
            <div className="alerts-list">
              {alerts.map(({ bus, policyKey, status, daysRemaining }, index) => (
                <div key={`${bus.id}-${policyKey}-${index}`} className={`alert-item ${status === 'soon' ? 'alert-item-soon' : ''}`}>
                  <div className="alert-item-left">
                    {status === 'expired' || status === 'today' ? (
                      <ShieldAlert size={20} className="alert-item-icon-danger" />
                    ) : (
                      <AlertTriangle size={20} className="alert-item-icon-warning" />
                    )}
                    <div className="alert-item-details">
                      <div className="alert-title">
                        {bus.plate} — {policyNames[policyKey]}
                      </div>
                      <div className="alert-subtitle">
                        {bus.operator} ({bus.brand}) • Bitiş Tarihi: {formatDate(bus.policies[policyKey].endDate)}
                      </div>
                    </div>
                  </div>

                  <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
                    {status === 'expired' && (
                      <span className="alert-badge alert-badge-danger">Günü Geçti ({Math.abs(daysRemaining)} gün)</span>
                    )}
                    {status === 'today' && (
                      <span className="alert-badge alert-badge-danger" style={{ animation: 'pulse-border 1.5s infinite alternate' }}>Bugün Bitiyor!</span>
                    )}
                    {status === 'soon' && (
                      <span className="alert-badge alert-badge-warning">{daysRemaining} Gün Kaldı</span>
                    )}
                    {isSuperAdmin && (
                      <button className="btn-primary" onClick={() => onEditBus(bus)} style={{ padding: '0.4rem 0.8rem', fontSize: '0.8rem' }}>
                        Güncelle
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Info panel */}
        <div className="card" style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
          <h3 style={{ fontFamily: 'var(--font-display)', fontWeight: 700, fontSize: '1.2rem' }}>
            Sistem Hakkında
          </h3>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem', fontSize: '0.875rem', lineHeight: '1.5', color: 'var(--text-secondary)' }}>
            <p>
              Bu sistem, şehirlerarası otobüslerin zorunlu trafik sigortası, kasko ve koltuk sigortalarını takip etmek için tasarlanmıştır.
            </p>
            <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-start' }}>
              <span className="status-badge today" style={{ padding: '0.2rem', width: '8px', height: '8px', flexShrink: 0, marginTop: '5px' }}></span>
              <div>
                <strong style={{ color: 'var(--text-primary)' }}>Kırmızı & Yanıp Sönen:</strong> Süresi bugün dolan veya geçmiş poliçeler.
              </div>
            </div>
            <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-start' }}>
              <span className="status-badge soon" style={{ padding: '0.2rem', width: '8px', height: '8px', flexShrink: 0, marginTop: '5px' }}></span>
              <div>
                <strong style={{ color: 'var(--text-primary)' }}>Sarı / Turuncu:</strong> Bitişine {SOON_THRESHOLD_DAYS} gün veya daha az kalan poliçeler.
              </div>
            </div>
            <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-start' }}>
              <span className="status-badge active" style={{ padding: '0.2rem', width: '8px', height: '8px', flexShrink: 0, marginTop: '5px' }}></span>
              <div>
                <strong style={{ color: 'var(--text-primary)' }}>Yeşil:</strong> {SOON_THRESHOLD_DAYS} günden fazla süresi olan güvenli poliçeler.
              </div>
            </div>
            <hr style={{ borderColor: 'var(--border-color)', margin: '0.5rem 0' }} />
            <p style={{ fontSize: '0.8rem', fontStyle: 'italic' }}>
              * E-posta uyarılarının çalışabilmesi için <strong>Ayarlar</strong> sekmesinden SMTP sunucu ayarlarınızı tamamlamayı unutmayın.
            </p>
          </div>
        </div>
      </div>
    </div>
  );
};
