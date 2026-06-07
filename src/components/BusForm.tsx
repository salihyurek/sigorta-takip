import { useState, useEffect, type FormEvent, type ChangeEvent } from 'react';
import type { Bus } from '../types';
import { X, Calendar, ShieldCheck, FileText, Bus as BusIcon } from 'lucide-react';

interface BusFormProps {
  bus: Bus | null; // Null if adding new bus, populated if editing
  onSave: (busData: Omit<Bus, 'id'> & { id?: string }) => Promise<void>;
  onClose: () => void;
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback;
}

export const BusForm = ({ bus, onSave, onClose }: BusFormProps) => {
  const [plate, setPlate] = useState('');
  const [brand, setBrand] = useState('');
  const [operator, setOperator] = useState('');
  
  // Policies State
  const [trafikStart, setTrafikStart] = useState('');
  const [trafikEnd, setTrafikEnd] = useState('');
  
  const [kaskoStart, setKaskoStart] = useState('');
  const [kaskoEnd, setKaskoEnd] = useState('');
  
  const [koltukStart, setKoltukStart] = useState('');
  const [koltukEnd, setKoltukEnd] = useState('');

  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Close on Escape key
  useEffect(() => {
    const handleEsc = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !submitting) {
        onClose();
      }
    };
    document.addEventListener('keydown', handleEsc);
    return () => document.removeEventListener('keydown', handleEsc);
  }, [onClose, submitting]);

  // Initialize form with bus details if editing
  useEffect(() => {
    if (bus) {
      setPlate(bus.plate);
      setBrand(bus.brand);
      setOperator(bus.operator);
      
      setTrafikStart(bus.policies.trafik.startDate);
      setTrafikEnd(bus.policies.trafik.endDate);
      
      setKaskoStart(bus.policies.kasko.startDate);
      setKaskoEnd(bus.policies.kasko.endDate);
      
      setKoltukStart(bus.policies.koltuk.startDate);
      setKoltukEnd(bus.policies.koltuk.endDate);
    } else {
      // Set defaults for new bus: StartDate = Today, EndDate = Today + 1 year
      const today = new Date();
      const nextYear = new Date();
      nextYear.setFullYear(today.getFullYear() + 1);
      
      const toDateString = (d: Date) => d.toISOString().split('T')[0];
      
      const todayStr = toDateString(today);
      const nextYearStr = toDateString(nextYear);
      
      setTrafikStart(todayStr);
      setTrafikEnd(nextYearStr);
      
      setKaskoStart(todayStr);
      setKaskoEnd(nextYearStr);
      
      setKoltukStart(todayStr);
      setKoltukEnd(nextYearStr);
    }
  }, [bus]);

  // Plate validation helper (Turkish Plate Format)
  // Standard formats: 34 A 1234, 34 AB 123, 34 ABC 12
  const validatePlate = (p: string) => {
    const cleanPlate = p.replace(/\s+/g, '').toUpperCase();
    const plateRegex = /^([0-7][0-9]|8[0-1])[A-Z]{1,3}[0-9]{2,4}$/;
    return plateRegex.test(cleanPlate);
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    
    // Validations
    if (!plate || !brand || !operator) {
      setError('Lütfen tüm genel bilgileri doldurun.');
      return;
    }

    if (!validatePlate(plate)) {
      setError('Geçersiz plaka formatı! Örnek formatlar: 34 ABC 123, 06 X 9999, 35 AB 12');
      return;
    }

    // Date validations (End date must be after start date)
    const datePairs = [
      { name: 'Zorunlu Trafik Sigortası', start: trafikStart, end: trafikEnd },
      { name: 'Kasko', start: kaskoStart, end: kaskoEnd },
      { name: 'Koltuk Sigortası', start: koltukStart, end: koltukEnd }
    ];

    for (const pair of datePairs) {
      if (!pair.start || !pair.end) {
        setError(`Lütfen ${pair.name} için her iki tarihi de seçin.`);
        return;
      }
      
      if (new Date(pair.end) < new Date(pair.start)) {
        setError(`${pair.name} bitiş tarihi, başlangıç tarihinden önce olamaz.`);
        return;
      }
    }

    try {
      setSubmitting(true);
      
      const payload: Omit<Bus, 'id'> & { id?: string } = {
        plate: plate.toUpperCase().trim(),
        brand: brand.trim(),
        operator: operator.trim(),
        policies: {
          trafik: { startDate: trafikStart, endDate: trafikEnd, lastEmailedDate: bus ? bus.policies.trafik.lastEmailedDate : null },
          kasko: { startDate: kaskoStart, endDate: kaskoEnd, lastEmailedDate: bus ? bus.policies.kasko.lastEmailedDate : null },
          koltuk: { startDate: koltukStart, endDate: koltukEnd, lastEmailedDate: bus ? bus.policies.koltuk.lastEmailedDate : null }
        }
      };

      if (bus) {
        payload.id = bus.id;
      }

      await onSave(payload);
      onClose();
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Kayıt sırasında bir hata oluştu.'));
    } finally {
      setSubmitting(false);
    }
  };

  // Helper to format plate input while typing (adds a space after city code and letters)
  const handlePlateChange = (e: ChangeEvent<HTMLInputElement>) => {
    const value = e.target.value.toUpperCase();
    setPlate(value);
  };

  return (
    <div className="modal-overlay" onClick={(e) => { if (e.target === e.currentTarget && !submitting) onClose(); }}>
      <div className="modal-content">
        <div className="modal-header">
          <h3 className="modal-title" style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <BusIcon size={20} className="text-primary" />
            {bus ? 'Araç ve Poliçe Bilgilerini Düzenle' : 'Yeni Araç Ekle'}
          </h3>
          <button className="icon-btn" onClick={onClose}>
            <X size={20} />
          </button>
        </div>
        
        <form onSubmit={handleSubmit}>
          <div className="modal-body">
            {error && (
              <div style={{ padding: '0.75rem 1rem', background: 'var(--danger-bg)', border: '1px solid var(--danger-border)', color: 'var(--danger)', borderRadius: 'var(--radius-sm)', marginBottom: '1.25rem', fontSize: '0.85rem' }}>
                {error}
              </div>
            )}
            
            {/* General Fields */}
            <div className="form-grid">
              <div className="form-group">
                <label className="form-label">Plaka</label>
                <input
                  type="text"
                  placeholder="34 ABC 123"
                  className="form-control"
                  value={plate}
                  onChange={handlePlateChange}
                  required
                />
              </div>

              <div className="form-group">
                <label className="form-label">Marka / Model</label>
                <input
                  type="text"
                  placeholder="Mercedes-Benz Travego"
                  className="form-control"
                  value={brand}
                  onChange={(e) => setBrand(e.target.value)}
                  required
                />
              </div>

              <div className="form-group form-grid-full">
                <label className="form-label">Firma / Acente Adı</label>
                <input
                  type="text"
                  placeholder="Metro Turizm"
                  className="form-control"
                  value={operator}
                  onChange={(e) => setOperator(e.target.value)}
                  required
                />
              </div>
            </div>

            {/* Policy 1: Trafik */}
            <div className="policy-form-section">
              <div className="policy-form-title">
                <ShieldCheck size={16} />
                Zorunlu Trafik Sigortası
              </div>
              <div className="date-inputs">
                <div className="form-group">
                  <label className="form-label">
                    <Calendar size={12} /> Başlangıç Tarihi
                  </label>
                  <input
                    type="date"
                    className="form-control"
                    value={trafikStart}
                    onChange={(e) => setTrafikStart(e.target.value)}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">
                    <Calendar size={12} /> Bitiş Tarihi
                  </label>
                  <input
                    type="date"
                    className="form-control"
                    value={trafikEnd}
                    onChange={(e) => setTrafikEnd(e.target.value)}
                    required
                  />
                </div>
              </div>
            </div>

            {/* Policy 2: Kasko */}
            <div className="policy-form-section">
              <div className="policy-form-title">
                <ShieldCheck size={16} />
                Kasko
              </div>
              <div className="date-inputs">
                <div className="form-group">
                  <label className="form-label">
                    <Calendar size={12} /> Başlangıç Tarihi
                  </label>
                  <input
                    type="date"
                    className="form-control"
                    value={kaskoStart}
                    onChange={(e) => setKaskoStart(e.target.value)}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">
                    <Calendar size={12} /> Bitiş Tarihi
                  </label>
                  <input
                    type="date"
                    className="form-control"
                    value={kaskoEnd}
                    onChange={(e) => setKaskoEnd(e.target.value)}
                    required
                  />
                </div>
              </div>
            </div>

            {/* Policy 3: Koltuk */}
            <div className="policy-form-section">
              <div className="policy-form-title">
                <FileText size={16} />
                Koltuk Sigortası (Ferdi Kaza)
              </div>
              <div className="date-inputs">
                <div className="form-group">
                  <label className="form-label">
                    <Calendar size={12} /> Başlangıç Tarihi
                  </label>
                  <input
                    type="date"
                    className="form-control"
                    value={koltukStart}
                    onChange={(e) => setKoltukStart(e.target.value)}
                    required
                  />
                </div>
                <div className="form-group">
                  <label className="form-label">
                    <Calendar size={12} /> Bitiş Tarihi
                  </label>
                  <input
                    type="date"
                    className="form-control"
                    value={koltukEnd}
                    onChange={(e) => setKoltukEnd(e.target.value)}
                    required
                  />
                </div>
              </div>
            </div>

          </div>
          
          <div className="modal-footer">
            <button type="button" className="btn-secondary" onClick={onClose} disabled={submitting}>
              Vazgeç
            </button>
            <button type="submit" className="btn-primary" disabled={submitting}>
              {submitting ? 'Kaydediliyor...' : bus ? 'Güncelle' : 'Kaydet'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
