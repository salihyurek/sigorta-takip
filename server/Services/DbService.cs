using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using SigortaTakip.Models;

namespace SigortaTakip.Services
{
    public class DbService
    {
        private readonly string _dbDir;
        private readonly string _dbFile;
        private readonly object _lock = new();
        private readonly TimeService _time;
        private readonly IDataProtector _protector;

        public string SuperadminEmail { get; private set; }
        private readonly string _superadminPassword;

        // Marks a stored SMTP password as encrypted so we can distinguish it from
        // legacy plaintext values written before encryption-at-rest was added.
        private const string EncPrefix = "enc:v1:";

        public DbService(TimeService time, IDataProtectionProvider protectionProvider)
        {
            _time = time;
            _protector = protectionProvider.CreateProtector("SigortaTakip.SmtpPassword.v1");

            // Allow overriding where data lives so it can point at a mounted
            // persistent disk (e.g. Render). Defaults to ../data next to the app.
            var dataDir = Environment.GetEnvironmentVariable("DATA_DIR");
            _dbDir = string.IsNullOrWhiteSpace(dataDir)
                ? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "data"))
                : Path.GetFullPath(dataDir);
            _dbFile = Path.Combine(_dbDir, "db.json");

            var envEmail = Environment.GetEnvironmentVariable("SUPERADMIN_EMAIL");
            var envPassword = Environment.GetEnvironmentVariable("SUPERADMIN_PASSWORD");
            var isProduction = string.Equals(
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                "Production", StringComparison.OrdinalIgnoreCase);

            if (isProduction && (string.IsNullOrWhiteSpace(envEmail) || string.IsNullOrWhiteSpace(envPassword)))
            {
                // Fail hard instead of silently booting with predictable default credentials.
                throw new InvalidOperationException(
                    "SUPERADMIN_EMAIL and SUPERADMIN_PASSWORD must be set in Production. " +
                    "Refusing to start with default credentials.");
            }

            SuperadminEmail = string.IsNullOrWhiteSpace(envEmail) ? "admin@sigortatakip.local" : envEmail;
            _superadminPassword = string.IsNullOrWhiteSpace(envPassword) ? "Admin123!Change" : envPassword;

            if (string.IsNullOrWhiteSpace(envEmail) || string.IsNullOrWhiteSpace(envPassword))
            {
                Console.WriteLine("[Db] WARNING: SUPERADMIN_EMAIL/PASSWORD not set. Using development defaults.");
            }

            InitDb();
            EnsureSeededAndMigrated();
        }

        private void InitDb()
        {
            lock (_lock)
            {
                if (!Directory.Exists(_dbDir))
                {
                    Directory.CreateDirectory(_dbDir);
                }

                if (!File.Exists(_dbFile))
                {
                    var defaultData = new DatabaseData
                    {
                        Settings = new Settings(),
                        Buses = GetSampleBuses(),
                        Users = new List<User>
                        {
                            new()
                            {
                                Id = "user-superadmin",
                                Email = SuperadminEmail,
                                Password = BCrypt.Net.BCrypt.HashPassword(_superadminPassword, 10),
                                Role = "superadmin",
                                ResetToken = null,
                                ResetTokenExpiry = null
                            }
                        }
                    };
                    WriteDb(defaultData);
                }
            }
        }

        /// <summary>
        /// One-time migration/seeding run at startup. Kept out of ReadDb so that
        /// reads are side-effect free (the scheduler reads frequently).
        /// </summary>
        private void EnsureSeededAndMigrated()
        {
            lock (_lock)
            {
                DatabaseData parsed;
                try
                {
                    // Use the shared decrypt-on-read path. Reading the raw file here (without
                    // decrypting SmtpPass) and then WriteDb-ing it on a migration would double-
                    // encrypt the SMTP password ("enc:v1:" + Protect("enc:v1:...")) and corrupt it.
                    parsed = ReadDbCore();
                }
                catch
                {
                    return; // ReadDb handles corruption/backup on demand
                }

                bool migrated = false;
                parsed.Users ??= new List<User>();

                var superAdminExists = false;
                foreach (var u in parsed.Users)
                {
                    if (string.Equals(u.Email, SuperadminEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        superAdminExists = true;
                        if (u.Role != "superadmin") { u.Role = "superadmin"; migrated = true; }
                        break;
                    }
                }

                if (!superAdminExists)
                {
                    parsed.Users.Add(new User
                    {
                        Id = "user-superadmin",
                        Email = SuperadminEmail,
                        Password = BCrypt.Net.BCrypt.HashPassword(_superadminPassword, 10),
                        Role = "superadmin",
                        ResetToken = null,
                        ResetTokenExpiry = null
                    });
                    migrated = true;
                }

                // Remove the legacy default admin account if it lingers from old versions.
                int oldAdminIndex = parsed.Users.FindIndex(u =>
                    string.Equals(u.Email, "admin@sigortatakip.com", StringComparison.OrdinalIgnoreCase));
                if (oldAdminIndex != -1) { parsed.Users.RemoveAt(oldAdminIndex); migrated = true; }

                foreach (var u in parsed.Users)
                {
                    if (string.IsNullOrEmpty(u.Role)) { u.Role = "viewer"; migrated = true; }
                }

                if (migrated) WriteDb(parsed);
            }
        }

        /// <summary>Core read; assumes the lock is held. Throws if the file is unreadable
        /// or malformed (callers decide how to react).</summary>
        private DatabaseData ReadDbCore()
        {
            var json = File.ReadAllText(_dbFile);
            var parsed = JsonSerializer.Deserialize<DatabaseData>(json) ?? new DatabaseData();
            parsed.Users ??= new List<User>();
            parsed.Buses ??= new List<Bus>();
            parsed.Settings ??= new Settings();

            parsed.Settings.SmtpPass = DecryptPassword(parsed.Settings.SmtpPass);
            return parsed;
        }

        /// <summary>Snapshot the (presumably corrupt) db file aside for manual recovery.</summary>
        private void BackupCorruptDb(Exception ex)
        {
            Console.WriteLine($"Error reading db.json: {ex.Message}");
            try
            {
                var backupPath = $"{_dbFile}.corrupt.{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                if (File.Exists(_dbFile))
                {
                    File.Copy(_dbFile, backupPath);
                    Console.WriteLine($"Corrupted db backed up to: {backupPath}");
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>Pure read. Returns settings with the SMTP password decrypted for in-app use.
        /// On corruption it backs the file up and returns empty data (read-only callers degrade
        /// gracefully); writers must use <see cref="Update"/>, which refuses to overwrite.</summary>
        public DatabaseData ReadDb()
        {
            lock (_lock)
            {
                try
                {
                    return ReadDbCore();
                }
                catch (Exception ex)
                {
                    BackupCorruptDb(ex);
                    return new DatabaseData();
                }
            }
        }

        /// <summary>Atomic write. Encrypts the SMTP password at rest without mutating the caller's object.</summary>
        public bool WriteDb(DatabaseData data)
        {
            lock (_lock)
            {
                if (!Directory.Exists(_dbDir)) Directory.CreateDirectory(_dbDir);

                // ??= so a null Settings is replaced on the object we serialize; a detached
                // placeholder would silently drop the encrypted password from the file.
                var settings = data.Settings ??= new Settings();
                var plainPass = settings.SmtpPass;
                try
                {
                    settings.SmtpPass = EncryptPassword(plainPass);

                    var tempFile = $"{_dbFile}.tmp";
                    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(tempFile, json);
                    File.Move(tempFile, _dbFile, overwrite: true);
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error writing to db.json: {ex.Message}");
                    return false;
                }
                finally
                {
                    // Restore plaintext so the in-memory object stays usable by the caller.
                    settings.SmtpPass = plainPass;
                }
            }
        }

        /// <summary>
        /// Atomically read-modify-write the database under a single lock. The previous
        /// pattern of calling ReadDb() then WriteDb() from a controller left a TOCTOU
        /// window where two concurrent requests could both pass a uniqueness check (e.g.
        /// duplicate plate) or clobber each other's edits. The mutate callback receives
        /// the freshly-read data (SMTP password decrypted) and returns true if it changed
        /// anything; only then is the result persisted. The lock is reentrant so the
        /// nested ReadDb/WriteDb calls are safe on the same thread.
        /// </summary>
        public bool Update(Func<DatabaseData, bool> mutate)
        {
            lock (_lock)
            {
                DatabaseData data;
                try
                {
                    data = ReadDbCore();
                }
                catch (Exception ex)
                {
                    // Refuse to overwrite an unreadable/corrupt db with partial data — that
                    // would turn a recoverable read error into permanent data loss. The
                    // backup preserves the original; the caller gets false and surfaces 500.
                    BackupCorruptDb(ex);
                    Console.WriteLine("[Db] Aborting Update: database is unreadable, not overwriting.");
                    return false;
                }

                var changed = mutate(data);
                if (!changed) return true;
                return WriteDb(data);
            }
        }

        private string EncryptPassword(string? plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try { return EncPrefix + _protector.Protect(plain); }
            catch { return plain ?? ""; }
        }

        private string DecryptPassword(string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            if (!stored.StartsWith(EncPrefix)) return stored; // legacy plaintext
            try { return _protector.Unprotect(stored.Substring(EncPrefix.Length)); }
            catch
            {
                Console.WriteLine("[Db] WARNING: failed to decrypt stored SMTP password (key mismatch?).");
                return "";
            }
        }

        private List<Bus> GetSampleBuses()
        {
            var today = _time.TodayString;
            var in14Days = _time.DateString(14);
            var in6Days = _time.DateString(6);
            var activeDate = _time.DateString(180);
            var expiredDate = _time.DateString(-17);

            return new List<Bus>
            {
                new()
                {
                    Id = "bus-1",
                    Plate = "34 MET 1923",
                    Brand = "Mercedes-Benz Travego 16 SHD",
                    Operator = "Metro Turizm",
                    Policies = new Dictionary<string, Policy>
                    {
                        { "trafik", new Policy { StartDate = "2025-06-06", EndDate = today, LastEmailedDate = null } },
                        { "kasko", new Policy { StartDate = "2025-12-15", EndDate = activeDate, LastEmailedDate = null } },
                        { "koltuk", new Policy { StartDate = "2025-06-20", EndDate = in14Days, LastEmailedDate = null } }
                    }
                },
                new()
                {
                    Id = "bus-2",
                    Plate = "06 KAM 1926",
                    Brand = "MAN Lion's Coach",
                    Operator = "Kamil Koç",
                    Policies = new Dictionary<string, Policy>
                    {
                        { "trafik", new Policy { StartDate = "2025-06-12", EndDate = in6Days, LastEmailedDate = null } },
                        { "kasko", new Policy { StartDate = "2025-06-06", EndDate = today, LastEmailedDate = null } },
                        { "koltuk", new Policy { StartDate = "2025-10-30", EndDate = activeDate, LastEmailedDate = null } }
                    }
                },
                new()
                {
                    Id = "bus-3",
                    Plate = "35 PAM 1962",
                    Brand = "Mercedes-Benz Tourismo",
                    Operator = "Pamukkale Turizm",
                    Policies = new Dictionary<string, Policy>
                    {
                        { "trafik", new Policy { StartDate = "2025-05-20", EndDate = expiredDate, LastEmailedDate = null } },
                        { "kasko", new Policy { StartDate = "2025-07-22", EndDate = activeDate, LastEmailedDate = null } },
                        { "koltuk", new Policy { StartDate = "2025-06-06", EndDate = today, LastEmailedDate = null } }
                    }
                },
                new()
                {
                    Id = "bus-4",
                    Plate = "50 NEV 050",
                    Brand = "Setra S 516 HDH",
                    Operator = "Nevşehir Seyahat",
                    Policies = new Dictionary<string, Policy>
                    {
                        { "trafik", new Policy { StartDate = "2025-08-10", EndDate = activeDate, LastEmailedDate = null } },
                        { "kasko", new Policy { StartDate = "2025-08-10", EndDate = activeDate, LastEmailedDate = null } },
                        { "koltuk", new Policy { StartDate = "2025-08-10", EndDate = activeDate, LastEmailedDate = null } }
                    }
                }
            };
        }
    }
}
