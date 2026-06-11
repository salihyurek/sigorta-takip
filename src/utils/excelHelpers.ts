import type { Bus } from '../types';

// `xlsx` is large; load it on demand so it stays out of the initial bundle and is
// only pulled in when the user actually imports/exports an Excel file.
type XlsxModule = typeof import('xlsx');
let xlsxPromise: Promise<XlsxModule> | null = null;
const loadXlsx = (): Promise<XlsxModule> => (xlsxPromise ??= import('xlsx'));

// Robust helper to parse and format date values from Excel.
// Exported for unit testing; the serial-number branch is timezone-sensitive.
export const formatExcelDate = (val: unknown): string => {
  if (val === undefined || val === null) return '';
  
  // Handle Excel Serial Date numbers
  if (typeof val === 'number') {
    // 25569 = days between 1899-12-31 (Excel epoch, incl. its 1900 leap-year bug) and
    // the Unix epoch. The result is a UTC midnight timestamp, so read it back with the
    // UTC getters — using the local getters would shift the date a day in UTC-negative
    // timezones (e.g. the Americas).
    const date = new Date(Math.round((val - 25569) * 86400 * 1000));
    if (isNaN(date.getTime())) return '';
    return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}-${String(date.getUTCDate()).padStart(2, '0')}`;
  }
  
  const str = String(val).trim();
  if (!str) return '';

  // Case 1: YYYY-MM-DD format already
  if (/^\d{4}-\d{2}-\d{2}$/.test(str)) {
    return str;
  }

  // Case 2: DD.MM.YYYY or DD/MM/YYYY format (Very common in Turkey)
  const dmyMatch = str.match(/^(\d{1,2})[./-](\d{1,2})[./-](\d{4})$/);
  if (dmyMatch) {
    const [, d, m, y] = dmyMatch;
    return `${y}-${m.padStart(2, '0')}-${d.padStart(2, '0')}`;
  }

  // Case 3: Try standard JavaScript Date parse
  const timestamp = Date.parse(str);
  if (!isNaN(timestamp)) {
    const date = new Date(timestamp);
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  }

  return '';
};

// Validate Turkish Plate formats (e.g., 34 ABC 123)
const validatePlate = (plate: string): boolean => {
  const clean = plate.replace(/\s+/g, '').toUpperCase();
  const plateRegex = /^([0-7][0-9]|8[0-1])[A-Z]{1,3}[0-9]{2,4}$/;
  return plateRegex.test(clean);
};

// Export buses data to Excel (.xlsx) file
export const exportBusesToExcel = async (buses: Bus[]) => {
  const XLSX = await loadXlsx();
  const data = buses.map((bus) => ({
    'Plaka': bus.plate,
    'Marka / Model': bus.brand,
    'Firma / Acente': bus.operator,
    'Zorunlu Trafik Sigortası Başlangıç': bus.policies.trafik.startDate,
    'Zorunlu Trafik Sigortası Bitiş': bus.policies.trafik.endDate,
    'Kasko Başlangıç': bus.policies.kasko.startDate,
    'Kasko Bitiş': bus.policies.kasko.endDate,
    'Koltuk Sigortası Başlangıç': bus.policies.koltuk.startDate,
    'Koltuk Bitiş': bus.policies.koltuk.endDate
  }));

  // Create worksheet and workbook
  const worksheet = XLSX.utils.json_to_sheet(data);
  const workbook = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(workbook, worksheet, 'Araçlar');

  // Define column widths for premium visual layout
  const colWidths = [
    { wch: 15 }, // Plaka
    { wch: 25 }, // Marka / Model
    { wch: 25 }, // Firma / Acente
    { wch: 32 }, // Trafik Başlangıç
    { wch: 28 }, // Trafik Bitiş
    { wch: 22 }, // Kasko Başlangıç
    { wch: 18 }, // Kasko Bitiş
    { wch: 24 }, // Koltuk Başlangıç
    { wch: 20 }  // Koltuk Bitiş
  ];
  worksheet['!cols'] = colWidths;

  // Save/Download Excel file
  XLSX.writeFile(workbook, `sigorta_takip_araclar_${new Date().toISOString().split('T')[0]}.xlsx`);
};

// Parse Excel file and extract buses array
export const importBusesFromExcel = (file: File): Promise<Omit<Bus, 'id'>[]> => {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();

    reader.onload = async (e) => {
      try {
        const data = e.target?.result;
        if (!data) {
          throw new Error('Dosya okunamadı.');
        }

        const XLSX = await loadXlsx();
        const workbook = XLSX.read(data, { type: 'array' });
        const sheetName = workbook.SheetNames[0];
        const worksheet = workbook.Sheets[sheetName];
        
        // Convert sheet to JSON array
        const rows = XLSX.utils.sheet_to_json(worksheet, { header: 1 }) as unknown[][];
        
        if (rows.length <= 1) {
          throw new Error('Excel dosyasında araç verisi bulunamadı.');
        }

        const headers = rows[0].map((h: unknown) => String(h).trim().toLowerCase());
        const buses: Omit<Bus, 'id'>[] = [];

        // Dynamic header indexing
        const getColIndex = (names: string[]): number => {
          return headers.findIndex((h: string) => names.includes(h));
        };

        const idxPlate = getColIndex(['plaka', 'araç plakası']);
        const idxBrand = getColIndex(['marka / model', 'marka', 'model', 'marka/model']);
        const idxOperator = getColIndex(['firma / acente', 'firma', 'acente', 'operatör', 'firma/acente']);
        
        const idxTrafikStart = getColIndex(['zorunlu trafik sigortası başlangıç', 'trafik başlangıç', 'trafik sigortası başlangıç']);
        const idxTrafikEnd = getColIndex(['zorunlu trafik sigortası bitiş', 'trafik bitiş', 'trafik sigortası bitiş']);
        
        const idxKaskoStart = getColIndex(['kasko başlangıç', 'kasko start']);
        const idxKaskoEnd = getColIndex(['kasko bitiş', 'kasko end']);
        
        const idxKoltukStart = getColIndex(['koltuk sigortası başlangıç', 'koltuk başlangıç']);
        const idxKoltukEnd = getColIndex(['koltuk sigortası bitiş', 'koltuk bitiş']);

        if (idxPlate === -1 || idxBrand === -1 || idxOperator === -1) {
          throw new Error('Gerekli sütunlar bulunamadı! Lütfen Plaka, Marka / Model ve Firma / Acente sütunlarının bulunduğundan emin olun.');
        }

        // Report missing date columns up front with their names — otherwise every row
        // would fail with a misleading "dates missing" error.
        const dateColumns: Array<[string, number]> = [
          ['Zorunlu Trafik Sigortası Başlangıç', idxTrafikStart],
          ['Zorunlu Trafik Sigortası Bitiş', idxTrafikEnd],
          ['Kasko Başlangıç', idxKaskoStart],
          ['Kasko Bitiş', idxKaskoEnd],
          ['Koltuk Sigortası Başlangıç', idxKoltukStart],
          ['Koltuk Sigortası Bitiş', idxKoltukEnd]
        ];
        const missingCols = dateColumns.filter(([, idx]) => idx === -1).map(([name]) => name);
        if (missingCols.length > 0) {
          throw new Error(`Gerekli tarih sütunları bulunamadı: ${missingCols.join(', ')}`);
        }

        for (let i = 1; i < rows.length; i++) {
          const row = rows[i];
          if (!row || row.length === 0) continue;

          const rawPlate = String(row[idxPlate] || '').trim();
          if (!rawPlate) continue; // Skip empty rows

          if (!validatePlate(rawPlate)) {
            throw new Error(`Satır ${i + 1}: Geçersiz plaka formatı ("${rawPlate}"). Örnek: 34 ABC 123`);
          }

          const brand = String(row[idxBrand] || '').trim();
          const operator = String(row[idxOperator] || '').trim();

          if (!brand || !operator) {
            throw new Error(`Satır ${i + 1}: Plakalı araç ("${rawPlate}") için Marka ve Firma / Acente alanları zorunludur.`);
          }

          // Parse and format dates, telling an empty cell apart from an unparseable one.
          const parseDateCell = (name: string, label: string, raw: unknown): string => {
            const rawStr = raw === undefined || raw === null ? '' : String(raw).trim();
            if (!rawStr) {
              throw new Error(`Satır ${i + 1}: ${rawPlate} için ${name} ${label} tarihi eksik.`);
            }
            const formatted = formatExcelDate(raw);
            if (!formatted) {
              throw new Error(`Satır ${i + 1}: ${rawPlate} için ${name} ${label} tarihi anlaşılamadı ("${rawStr}"). Beklenen format: GG.AA.YYYY`);
            }
            return formatted;
          };

          const trafikStart = parseDateCell('Zorunlu Trafik Sigortası', 'başlangıç', row[idxTrafikStart]);
          const trafikEnd = parseDateCell('Zorunlu Trafik Sigortası', 'bitiş', row[idxTrafikEnd]);
          const kaskoStart = parseDateCell('Kasko', 'başlangıç', row[idxKaskoStart]);
          const kaskoEnd = parseDateCell('Kasko', 'bitiş', row[idxKaskoEnd]);
          const koltukStart = parseDateCell('Koltuk Sigortası', 'başlangıç', row[idxKoltukStart]);
          const koltukEnd = parseDateCell('Koltuk Sigortası', 'bitiş', row[idxKoltukEnd]);

          // YYYY-MM-DD strings compare correctly as plain strings (same rule the
          // backend's ValidateBus uses), no Date parsing needed.
          const validateOrder = (name: string, start: string, end: string) => {
            if (end < start) {
              throw new Error(`Satır ${i + 1}: ${rawPlate} için ${name} bitiş tarihi başlangıç tarihinden önce olamaz.`);
            }
          };

          validateOrder('Zorunlu Trafik Sigortası', trafikStart, trafikEnd);
          validateOrder('Kasko', kaskoStart, kaskoEnd);
          validateOrder('Koltuk Sigortası', koltukStart, koltukEnd);

          buses.push({
            plate: rawPlate.toUpperCase(),
            brand,
            operator,
            policies: {
              trafik: { startDate: trafikStart, endDate: trafikEnd, lastEmailedDate: null },
              kasko: { startDate: kaskoStart, endDate: kaskoEnd, lastEmailedDate: null },
              koltuk: { startDate: koltukStart, endDate: koltukEnd, lastEmailedDate: null }
            }
          });
        }

        resolve(buses);
      } catch (err: unknown) {
        reject(err);
      }
    };

    reader.onerror = () => {
      reject(new Error('Dosya okuma hatası oluştu.'));
    };

    reader.readAsArrayBuffer(file);
  });
};
