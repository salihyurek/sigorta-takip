using System;
using System.IO;
using Microsoft.AspNetCore.DataProtection;
using SigortaTakip.Services;

namespace SigortaTakip.Tests
{
    public class DbServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly IDataProtectionProvider _provider;

        public DbServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "sigorta-db-tests-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("DATA_DIR", _tempDir);
            Environment.SetEnvironmentVariable("SUPERADMIN_EMAIL", "first@example.com");
            Environment.SetEnvironmentVariable("SUPERADMIN_PASSWORD", "Admin123!Change");
            // A provider with persisted keys, shared across "restarts" so encrypted values
            // stay decryptable (mirrors PersistKeysToFileSystem in Program.cs).
            _provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_tempDir, "keys")));
        }

        [Fact]
        public void SmtpPassword_SurvivesStartupMigration_WithoutDoubleEncryption()
        {
            const string smtpPass = "s3cr3t-smtp-pass";

            // First boot: persist an SMTP password (stored encrypted at rest).
            var db1 = new DbService(new TimeService(), _provider);
            Assert.True(db1.Update(d => { d.Settings.SmtpPass = smtpPass; return true; }));
            Assert.Equal(smtpPass, db1.ReadDb().Settings.SmtpPass);

            // Second boot with a different bootstrap admin forces a migration write (a new
            // superadmin is appended). Regression guard: that write must NOT double-encrypt
            // the already-encrypted SMTP password — it must still decrypt to the original.
            Environment.SetEnvironmentVariable("SUPERADMIN_EMAIL", "second@example.com");
            var db2 = new DbService(new TimeService(), _provider);

            Assert.Equal(smtpPass, db2.ReadDb().Settings.SmtpPass);
        }

        [Fact]
        public void Update_RoundTripsSmtpPassword()
        {
            var db = new DbService(new TimeService(), _provider);
            Assert.True(db.Update(d => { d.Settings.SmtpPass = "pw-1"; return true; }));
            // A second update that doesn't touch the password must keep it intact (the
            // read decrypts, the write re-encrypts exactly once).
            Assert.True(db.Update(d => { d.Settings.SmtpHost = "smtp.example.com"; return true; }));
            Assert.Equal("pw-1", db.ReadDb().Settings.SmtpPass);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SUPERADMIN_EMAIL", null);
            Environment.SetEnvironmentVariable("SUPERADMIN_PASSWORD", null);
            Environment.SetEnvironmentVariable("DATA_DIR", null);
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
