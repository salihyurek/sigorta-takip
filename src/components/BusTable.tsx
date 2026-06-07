import { useState } from 'react';
import type { Bus } from '../types';
import { getPolicyStatus, getDaysRemaining, formatDate } from '../utils/dateHelpers';
import { Search, Edit2, Trash2, Shield, Calendar, AlertOctagon } from 'lucide-react';

interface BusTableProps {
  buses: Bus[];
  onEditBus: (bus: Bus) => void;
  onDeleteBus: (id: string) => void;
  isSuperAdmin?: boolean;
}

export const BusTable = ({ buses, onEditBus, onDeleteBus, isSuperAdmin = true }: BusTableProps) => {
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [sortBy, setSortBy] = useState<string>('plate');

  // Filter and search logic
  const filteredBuses = buses.filter((bus) => {
    // Search filter
    const matchesSearch = 
      bus.plate.toLowerCase().includes(searchTerm.toLowerCase()) ||
      bus.brand.toLowerCase().includes(searchTerm.toLowerCase()) ||
      bus.operator.toLowerCase().includes(searchTerm.toLowerCase());

    if (!matchesSearch) return false;

    // Status filter
    if (statusFilter === 'all') return true;

    const statuses = [
      getPolicyStatus(bus.policies?.trafik?.endDate || ''),
      getPolicyStatus(bus.policies?.kasko?.endDate || ''),
      getPolicyStatus(bus.policies?.koltuk?.endDate || '')
    ];

    if (statusFilter === 'expired') return statuses.includes('expired');
    if (statusFilter === 'today') return statuses.includes('today');
    if (statusFilter === 'soon') return statuses.includes('soon');
    if (statusFilter === 'active') {
      // Active means all three are active
      return !statuses.includes('expired') && !statuses.includes('today') && !statuses.includes('soon');
    }

    return true;
  });

  // Sorting logic
  const sortedBuses = [...filteredBuses].sort((a, b) => {
    if (sortBy === 'plate') {
      return a.plate.localeCompare(b.plate);
    }
    
    if (sortBy === 'operator') {
      return a.operator.localeCompare(b.operator);
    }
    
    // Sort by soonest policy expiry
    const getMinExpiryDays = (bus: Bus) => {
      return Math.min(
        getDaysRemaining(bus.policies?.trafik?.endDate || ''),
        getDaysRemaining(bus.policies?.kasko?.endDate || ''),
        getDaysRemaining(bus.policies?.koltuk?.endDate || '')
      );
    };
    
    return getMinExpiryDays(a) - getMinExpiryDays(b);
  });

  // Render Policy Cell
  const renderPolicyCell = (endDate: string) => {
    const status = getPolicyStatus(endDate);
    const days = getDaysRemaining(endDate);
    const formatted = formatDate(endDate);

    let statusText: string;
    let className: string;

    if (status === 'expired') {
      statusText = `Günü Geçti (${Math.abs(days)} Gün)`;
      className = 'status-badge expired';
    } else if (status === 'today') {
      statusText = 'Bugün Bitiyor!';
      className = 'status-badge today';
    } else if (status === 'soon') {
      statusText = `${days} Gün Kaldı`;
      className = 'status-badge soon';
    } else {
      statusText = 'Aktif';
      className = 'status-badge active';
    }

    return (
      <div className="policy-cell">
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
          <Calendar size={14} className="text-muted" />
          <span className="policy-date" style={{ color: status === 'today' ? 'var(--danger)' : status === 'expired' ? 'var(--danger)' : 'var(--text-primary)' }}>
            {formatted}
          </span>
        </div>
        <div>
          <span className={className}>
            {statusText}
          </span>
        </div>
      </div>
    );
  };

  const handleDelete = (id: string, plate: string) => {
    if (window.confirm(`"${plate}" plakalı aracı silmek istediğinizden emin misiniz?`)) {
      onDeleteBus(id);
    }
  };

  return (
    <div>
      {/* Search and Filters panel */}
      <div className="action-panel">
        <div className="search-filter-group">
          <div className="search-input-wrapper">
            <Search size={18} className="search-icon" />
            <input
              type="text"
              placeholder="Plaka, marka, model veya firma ara..."
              className="form-control"
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
            />
          </div>

          <div style={{ minWidth: '150px' }}>
            <select
              className="form-control select-control"
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="all">Tüm Durumlar</option>
              <option value="expired">Günü Geçenler var</option>
              <option value="today">Bugün Bitenler var</option>
              <option value="soon">15 Gün Kalanlar var</option>
              <option value="active">Tümü Güvenli (Aktif)</option>
            </select>
          </div>

          <div style={{ minWidth: '150px' }}>
            <select
              className="form-control select-control"
              value={sortBy}
              onChange={(e) => setSortBy(e.target.value)}
            >
              <option value="plate">Plakaya Göre Sırala</option>
              <option value="operator">Firmaya Göre Sırala</option>
              <option value="expiry">En Yakın Bitişe Göre</option>
            </select>
          </div>
        </div>
      </div>

      {/* Table grid */}
      {sortedBuses.length === 0 ? (
        <div className="empty-state">
          <AlertOctagon size={48} className="empty-state-icon" />
          <h3 className="empty-state-title">Araç Bulunamadı</h3>
          <p className="empty-state-desc">
            Arama kriterlerinize uygun araç bulunamadı. Lütfen filtrelerinizi kontrol edin veya yeni bir araç ekleyin.
          </p>
        </div>
      ) : (
        <div className="table-container">
          <table className="bus-table">
            <thead>
              <tr>
                <th style={{ width: '22%' }}>Araç Detayları</th>
                <th style={{ width: '22%' }}>Zorunlu Trafik Sigortası</th>
                <th style={{ width: '22%' }}>Kasko</th>
                <th style={{ width: '22%' }}>Koltuk Sigortası</th>
                {isSuperAdmin && <th style={{ width: '12%', textAlign: 'right' }}>İşlemler</th>}
              </tr>
            </thead>
            <tbody>
              {sortedBuses.map((bus) => (
                <tr key={bus.id} className="bus-row">
                  <td>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                      <div>
                        <span className="plate-badge">
                          <Shield size={14} style={{ color: 'var(--primary)' }} />
                          {bus.plate}
                        </span>
                      </div>
                      <div>
                        <div className="brand-text">{bus.brand}</div>
                        <div className="operator-text">{bus.operator}</div>
                      </div>
                    </div>
                  </td>
                  <td>{renderPolicyCell(bus.policies?.trafik?.endDate || '')}</td>
                  <td>{renderPolicyCell(bus.policies?.kasko?.endDate || '')}</td>
                  <td>{renderPolicyCell(bus.policies?.koltuk?.endDate || '')}</td>
                  {isSuperAdmin && (
                    <td>
                      <div className="row-actions">
                        <button
                          className="icon-btn edit"
                          onClick={() => onEditBus(bus)}
                          title="Bilgileri Düzenle"
                        >
                          <Edit2 size={16} />
                        </button>
                        <button
                          className="icon-btn delete"
                          onClick={() => handleDelete(bus.id, bus.plate)}
                          title="Aracı Sil"
                        >
                          <Trash2 size={16} />
                        </button>
                      </div>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};
