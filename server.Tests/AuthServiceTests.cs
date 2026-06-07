using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SigortaTakip.Services;

namespace SigortaTakip.Tests
{
    public class AuthServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly AuthService _auth;

        public AuthServiceTests()
        {
            // Point session persistence at a throwaway dir so the real ../data is untouched.
            _tempDir = Path.Combine(Path.GetTempPath(), "sigorta-tests-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("DATA_DIR", _tempDir);
            _auth = new AuthService();
        }

        [Fact]
        public void HashToken_IsDeterministicAndHidesRawToken()
        {
            const string token = "deadbeefcafe";
            var a = AuthService.HashToken(token);
            var b = AuthService.HashToken(token);

            Assert.Equal(a, b);                         // deterministic
            Assert.NotEqual(token, a);                  // not the raw value
            Assert.Equal(64, a.Length);                 // SHA-256 hex
            Assert.NotEqual(a, AuthService.HashToken("other"));
        }

        [Fact]
        public void HashPassword_RoundTripsWithCompare()
        {
            var hash = _auth.HashPassword("Sifre123!");
            Assert.StartsWith("$2", hash);              // bcrypt
            Assert.True(_auth.ComparePassword("Sifre123!", hash));
            Assert.False(_auth.ComparePassword("yanlis", hash));
        }

        [Fact]
        public void ComparePassword_AcceptsLegacySha256Hashes()
        {
            // Mirrors the legacy scheme the migration path still supports:
            // SHA-256(password + salt) as lowercase hex.
            const string password = "EskiSifre1";
            const string legacySalt = "sigorta_takip_salt_2026";
            using var sha = SHA256.Create();
            var legacyHash = Convert.ToHexString(
                sha.ComputeHash(Encoding.UTF8.GetBytes(password + legacySalt))).ToLowerInvariant();

            Assert.True(_auth.ComparePassword(password, legacyHash));
            Assert.False(_auth.ComparePassword("wrong", legacyHash));
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
