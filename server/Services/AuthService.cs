using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SigortaTakip.Services
{
    public class SessionData
    {
        public string Email { get; set; } = "";
        public string Role { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class AuthService
    {
        private readonly ConcurrentDictionary<string, SessionData> _sessions = new();
        private readonly TimeSpan _sessionTtl = TimeSpan.FromHours(24);
        private readonly CancellationTokenSource _cts = new();
        private readonly string _sessionFile;
        private readonly object _fileLock = new();

        public AuthService()
        {
            // Persist sessions next to the database so they survive server restarts
            // (otherwise every redeploy / container restart logs everyone out).
            var dataDir = Environment.GetEnvironmentVariable("DATA_DIR");
            var dir = string.IsNullOrWhiteSpace(dataDir)
                ? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "data"))
                : Path.GetFullPath(dataDir);
            _sessionFile = Path.Combine(dir, "sessions.json");

            LoadSessions();

            // Periodically purge expired sessions (every hour).
            System.Threading.Tasks.Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try { await System.Threading.Tasks.Task.Delay(TimeSpan.FromHours(1), _cts.Token); }
                    catch (TaskCanceledException) { break; }
                    CleanExpiredSessions();
                }
            });
        }

        /// <summary>
        /// SHA-256 of a token, used as the at-rest key. Tokens are 256-bit random values
        /// so a fast hash is sufficient (no brute-forcing the preimage); this keeps the
        /// raw bearer/reset tokens out of sessions.json / db.json on disk. Shared with
        /// reset tokens so both are stored hashed.
        /// </summary>
        public static string HashToken(string token)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        }

        public string CreateSession(string email, string role)
        {
            byte[] randomBytes = new byte[32];
            RandomNumberGenerator.Fill(randomBytes);
            string token = Convert.ToHexString(randomBytes).ToLowerInvariant();

            // Persist only the hash; the raw token is returned to the client once.
            _sessions[HashToken(token)] = new SessionData
            {
                Email = email,
                Role = role,
                CreatedAt = DateTime.UtcNow
            };

            SaveSessions();
            return token;
        }

        public SessionData? ValidateSession(string token)
        {
            var key = HashToken(token);
            if (!_sessions.TryGetValue(key, out var session))
            {
                return null;
            }

            if (DateTime.UtcNow - session.CreatedAt > _sessionTtl)
            {
                if (_sessions.TryRemove(key, out _)) SaveSessions();
                return null;
            }

            return session;
        }

        public void DeleteSession(string token)
        {
            if (_sessions.TryRemove(HashToken(token), out _)) SaveSessions();
        }

        public void DeleteAllSessionsForUser(string email)
        {
            DeleteAllSessionsForUser(email, exceptToken: null);
        }

        /// <summary>
        /// Invalidate every session for a user, optionally keeping one token alive.
        /// Used after a self-initiated password change so the current user is not
        /// abruptly logged out of the session they are actively using.
        /// </summary>
        public void DeleteAllSessionsForUser(string email, string? exceptToken)
        {
            // Keys are token hashes, so hash the raw exception token before comparing.
            var exceptKey = string.IsNullOrEmpty(exceptToken) ? null : HashToken(exceptToken);
            var tokensToRemove = _sessions
                .Where(kvp => string.Equals(kvp.Value.Email, email, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .Where(t => exceptKey == null || !string.Equals(t, exceptKey, StringComparison.Ordinal))
                .ToList();

            bool changed = false;
            foreach (var token in tokensToRemove)
            {
                if (_sessions.TryRemove(token, out _)) changed = true;
            }
            if (changed) SaveSessions();
        }

        public string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password, 10);
        }

        public bool ComparePassword(string password, string hash)
        {
            // Support legacy SHA-256 hashes during migration
            if (hash != null && hash.Length == 64 && !hash.StartsWith("$2"))
            {
                using var sha = SHA256.Create();
                var legacySalt = "sigorta_takip_salt_2026";
                var bytes = Encoding.UTF8.GetBytes(password + legacySalt);
                var hashBytes = sha.ComputeHash(bytes);
                var legacyHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                return legacyHash == hash.ToLowerInvariant();
            }

            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch
            {
                return false;
            }
        }

        private void CleanExpiredSessions()
        {
            var now = DateTime.UtcNow;
            var expiredTokens = _sessions
                .Where(kvp => now - kvp.Value.CreatedAt > _sessionTtl)
                .Select(kvp => kvp.Key)
                .ToList();

            bool changed = false;
            foreach (var token in expiredTokens)
            {
                if (_sessions.TryRemove(token, out _)) changed = true;
            }
            if (changed) SaveSessions();
        }

        private void LoadSessions()
        {
            try
            {
                if (!File.Exists(_sessionFile)) return;
                var json = File.ReadAllText(_sessionFile);
                var stored = JsonSerializer.Deserialize<Dictionary<string, SessionData>>(json);
                if (stored == null) return;

                var now = DateTime.UtcNow;
                foreach (var kvp in stored)
                {
                    if (now - kvp.Value.CreatedAt <= _sessionTtl)
                    {
                        _sessions[kvp.Key] = kvp.Value;
                    }
                }
                Console.WriteLine($"[Auth] Restored {_sessions.Count} active session(s) from disk.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Could not load sessions: {ex.Message}");
            }
        }

        private void SaveSessions()
        {
            lock (_fileLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_sessionFile);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    var snapshot = new Dictionary<string, SessionData>(_sessions);
                    var tempFile = $"{_sessionFile}.tmp";
                    File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot));
                    File.Move(tempFile, _sessionFile, overwrite: true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Auth] Could not persist sessions: {ex.Message}");
                }
            }
        }
    }
}
