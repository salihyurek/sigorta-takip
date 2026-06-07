const fs = require('fs');
const path = require('path');
const bcrypt = require('bcryptjs');

const DB_DIR = path.join(__dirname, '..', 'data');
const DB_FILE = path.join(DB_DIR, 'db.json');

function hashPassword(password) {
  return bcrypt.hashSync(password, 10);
}

function comparePassword(password, hash) {
  // Support legacy SHA-256 hashes during migration
  if (hash && hash.length === 64 && !hash.startsWith('$2')) {
    const crypto = require('crypto');
    const legacySalt = 'sigorta_takip_salt_2026';
    const legacyHash = crypto.createHash('sha256').update(password + legacySalt).digest('hex');
    return legacyHash === hash;
  }
  return bcrypt.compareSync(password, hash);
}

// Ensure db directory and file exist
function initDb() {
  if (!fs.existsSync(DB_DIR)) {
    fs.mkdirSync(DB_DIR, { recursive: true });
  }

  if (!fs.existsSync(DB_FILE)) {
    const defaultData = {
      settings: {
        smtpHost: '',
        smtpPort: 587,
        smtpUser: '',
        smtpPass: '',
        senderEmail: '',
        receiverEmail: '',
        enableEmails: false
      },
      buses: getSampleBuses(),
      users: [
        {
          id: 'user-superadmin',
          email: 'salihyurek004@gmail.com',
          password: hashPassword('Ej+D8q6zhg3kRX*'),
          role: 'superadmin',
          resetToken: null,
          resetTokenExpiry: null
        }
      ]
    };
    fs.writeFileSync(DB_FILE, JSON.stringify(defaultData, null, 2), 'utf8');
  }
}

function getSampleBuses() {
  // Today's date is 2026-06-06 based on system time
  const today = '2026-06-06';
  
  return [
    {
      id: 'bus-1',
      plate: '34 MET 1923',
      brand: 'Mercedes-Benz Travego 16 SHD',
      operator: 'Metro Turizm',
      policies: {
        trafik: {
          startDate: '2025-06-06',
          endDate: today, // Expiring Today
          lastEmailedDate: null
        },
        kasko: {
          startDate: '2025-12-15',
          endDate: '2026-12-15', // Active
          lastEmailedDate: null
        },
        koltuk: {
          startDate: '2025-06-20',
          endDate: '2026-06-20', // Expiring in 14 days
          lastEmailedDate: null
        }
      }
    },
    {
      id: 'bus-2',
      plate: '06 KAM 1926',
      brand: 'MAN Lion\'s Coach',
      operator: 'Kamil Koç',
      policies: {
        trafik: {
          startDate: '2025-06-12',
          endDate: '2026-06-12', // Expiring in 6 days
          lastEmailedDate: null
        },
        kasko: {
          startDate: '2025-06-06',
          endDate: today, // Expiring Today
          lastEmailedDate: null
        },
        koltuk: {
          startDate: '2025-10-30',
          endDate: '2026-10-30', // Active
          lastEmailedDate: null
        }
      }
    },
    {
      id: 'bus-3',
      plate: '35 PAM 1962',
      brand: 'Mercedes-Benz Tourismo',
      operator: 'Pamukkale Turizm',
      policies: {
        trafik: {
          startDate: '2025-05-20',
          endDate: '2026-05-20', // Expired already
          lastEmailedDate: null
        },
        kasko: {
          startDate: '2025-07-22',
          endDate: '2026-07-22', // Active
          lastEmailedDate: null
        },
        koltuk: {
          startDate: '2025-06-06',
          endDate: today, // Expiring Today
          lastEmailedDate: null
        }
      }
    },
    {
      id: 'bus-4',
      plate: '50 NEV 050',
      brand: 'Setra S 516 HDH',
      operator: 'Nevşehir Seyahat',
      policies: {
        trafik: {
          startDate: '2025-08-10',
          endDate: '2026-08-10', // Active
          lastEmailedDate: null
        },
        kasko: {
          startDate: '2025-08-10',
          endDate: '2026-08-10', // Active
          lastEmailedDate: null
        },
        koltuk: {
          startDate: '2025-08-10',
          endDate: '2026-08-10', // Active
          lastEmailedDate: null
        }
      }
    }
  ];
}

// Read database
function readDb() {
  initDb();
  try {
    const data = fs.readFileSync(DB_FILE, 'utf8');
    const parsed = JSON.parse(data);
    
    // Migration for existing database files
    let migrated = false;
    if (!parsed.users) {
      parsed.users = [];
      migrated = true;
    }
    
    // Check if salihyurek004@gmail.com is seeded
    const superAdminExists = parsed.users.some(u => u.email.toLowerCase() === 'salihyurek004@gmail.com');
    if (!superAdminExists) {
      parsed.users.push({
        id: 'user-superadmin',
        email: 'salihyurek004@gmail.com',
        password: hashPassword('Ej+D8q6zhg3kRX*'),
        role: 'superadmin',
        resetToken: null,
        resetTokenExpiry: null
      });
      migrated = true;
    }
    
    // Remove old default admin account if present
    const oldAdminIndex = parsed.users.findIndex(u => u.email.toLowerCase() === 'admin@sigortatakip.com');
    if (oldAdminIndex !== -1) {
      parsed.users.splice(oldAdminIndex, 1);
      migrated = true;
    }
    
    // Ensure all users have roles & reset fields
    parsed.users.forEach(u => {
      if (u.email.toLowerCase() === 'salihyurek004@gmail.com') {
        if (u.role !== 'superadmin') {
          u.role = 'superadmin';
          migrated = true;
        }
      } else {
        if (!u.role) {
          u.role = 'viewer';
          migrated = true;
        }
      }
      
      if (u.resetToken === undefined) {
        u.resetToken = null;
        u.resetTokenExpiry = null;
        migrated = true;
      }
    });
    
    if (migrated) {
      writeDb(parsed);
    }
    
    return parsed;
  } catch (err) {
    console.error('Error reading db.json:', err);
    // Create backup of corrupted file
    try {
      const backupPath = DB_FILE + '.corrupt.' + Date.now();
      if (fs.existsSync(DB_FILE)) {
        fs.copyFileSync(DB_FILE, backupPath);
        console.error('Corrupted db backed up to:', backupPath);
      }
    } catch (e) { /* ignore backup errors */ }
    return { settings: {}, buses: [], users: [] };
  }
}

// Write database (atomic write pattern)
function writeDb(data) {
  initDb();
  try {
    const tempFile = DB_FILE + '.tmp';
    fs.writeFileSync(tempFile, JSON.stringify(data, null, 2), 'utf8');
    fs.renameSync(tempFile, DB_FILE);
    return true;
  } catch (err) {
    console.error('Error writing to db.json:', err);
    return false;
  }
}

module.exports = {
  readDb,
  writeDb,
  hashPassword,
  comparePassword
};
