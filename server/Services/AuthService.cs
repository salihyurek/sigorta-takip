using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

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

        public AuthService()
        {
            // Start a background task to clean expired sessions periodically (every hour)
            System.Threading.Tasks.Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try { await System.Threading.Tasks.Task.Delay(TimeSpan.FromHours(1), _cts.Token); } catch (TaskCanceledException) { break; }
                    CleanExpiredSessions();
                }
            });
        }

        public string CreateSession(string email, string role)
        {
            // Generate a secure 32-byte hex token matching Node's crypto.randomBytes(32).toString('hex')
            byte[] randomBytes = new byte[32];
            RandomNumberGenerator.Fill(randomBytes);
            string token = Convert.ToHexString(randomBytes).ToLowerInvariant();

            var session = new SessionData
            {
                Email = email,
                Role = role,
                CreatedAt = DateTime.UtcNow
            };

            _sessions[token] = session;
            return token;
        }

        public SessionData? ValidateSession(string token)
        {
            if (!_sessions.TryGetValue(token, out var session))
            {
                return null;
            }

            if (DateTime.UtcNow - session.CreatedAt > _sessionTtl)
            {
                _sessions.TryRemove(token, out _);
                return null;
            }

            return session;
        }

        public void DeleteSession(string token)
        {
            _sessions.TryRemove(token, out _);
        }

        public void DeleteAllSessionsForUser(string email)
        {
            var tokensToRemove = _sessions
                .Where(kvp => string.Equals(kvp.Value.Email, email, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var token in tokensToRemove)
            {
                _sessions.TryRemove(token, out _);
            }
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

            foreach (var token in expiredTokens)
            {
                _sessions.TryRemove(token, out _);
            }
        }
    }
}
