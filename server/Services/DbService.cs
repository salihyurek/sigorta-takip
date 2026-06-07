using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SigortaTakip.Models;

namespace SigortaTakip.Services
{
    public class DbService
    {
        private readonly string _dbDir;
        private readonly string _dbFile;
        private readonly object _lock = new();

        public string SuperadminEmail { get; private set; }
        public string SuperadminPassword { get; private set; }

        public DbService()
        {
            _dbDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "data"));
            _dbFile = Path.Combine(_dbDir, "db.json");

            // Load superadmin config from env, fallback to defaults
            SuperadminEmail = Environment.GetEnvironmentVariable("SUPERADMIN_EMAIL") ?? "admin@sigortatakip.local";
            SuperadminPassword = Environment.GetEnvironmentVariable("SUPERADMIN_PASSWORD") ?? "Admin123!Change";

            InitDb();
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
                                Password = BCrypt.Net.BCrypt.HashPassword(SuperadminPassword, 10),
                                Role = "superadmin",
                                ResetToken = null,
                                ResetTokenExpiry = null
                            }
                        }
                    };
                    var json = JsonSerializer.Serialize(defaultData, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_dbFile, json);
                }
            }
        }

        public DatabaseData ReadDb()
        {
            lock (_lock)
            {
                InitDb();
                try
                {
                    var json = File.ReadAllText(_dbFile);
                    var parsed = JsonSerializer.Deserialize<DatabaseData>(json) ?? new DatabaseData();

                    bool migrated = false;
                    if (parsed.Users == null)
                    {
                        parsed.Users = new List<User>();
                        migrated = true;
                    }

                    // Check if superadmin is seeded
                    var superAdminExists = false;
                    foreach (var u in parsed.Users)
                    {
                        if (string.Equals(u.Email, SuperadminEmail, StringComparison.OrdinalIgnoreCase))
                        {
                            superAdminExists = true;
                            if (u.Role != "superadmin")
                            {
                                u.Role = "superadmin";
                                migrated = true;
                            }
                            break;
                        }
                    }

                    if (!superAdminExists)
                    {
                        parsed.Users.Add(new User
                        {
                            Id = "user-superadmin",
                            Email = SuperadminEmail,
                            Password = BCrypt.Net.BCrypt.HashPassword(SuperadminPassword, 10),
                            Role = "superadmin",
                            ResetToken = null,
                            ResetTokenExpiry = null
                        });
                        migrated = true;
                    }

                    // Remove old default admin account if present
                    int oldAdminIndex = parsed.Users.FindIndex(u => string.Equals(u.Email, "admin@sigortatakip.com", StringComparison.OrdinalIgnoreCase));
                    if (oldAdminIndex != -1)
                    {
                        parsed.Users.RemoveAt(oldAdminIndex);
                        migrated = true;
                    }

                    // Ensure all users have roles & reset fields
                    foreach (var u in parsed.Users)
                    {
                        if (string.Equals(u.Email, SuperadminEmail, StringComparison.OrdinalIgnoreCase))
                        {
                            if (u.Role != "superadmin")
                            {
                                u.Role = "superadmin";
                                migrated = true;
                            }
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(u.Role))
                            {
                                u.Role = "viewer";
                                migrated = true;
                            }
                        }
                    }

                    if (migrated)
                    {
                        WriteDb(parsed);
                    }

                    return parsed;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading db.json: {ex.Message}");
                    // Create backup of corrupted file
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

                    return new DatabaseData();
                }
            }
        }

        public bool WriteDb(DatabaseData data)
        {
            lock (_lock)
            {
                InitDb();
                try
                {
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
            }
        }

        private List<Bus> GetSampleBuses()
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var in14Days = DateTime.UtcNow.AddDays(14).ToString("yyyy-MM-dd");
            var in6Days = DateTime.UtcNow.AddDays(6).ToString("yyyy-MM-dd");
            var activeDate = DateTime.UtcNow.AddMonths(6).ToString("yyyy-MM-dd");
            var expiredDate = DateTime.UtcNow.AddDays(-17).ToString("yyyy-MM-dd");

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
