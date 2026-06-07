using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SigortaTakip.Models;
using SigortaTakip.Services;

namespace SigortaTakip.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : BaseApiController
    {
        private readonly MailService _mailService;

        public AuthController(DbService dbService, AuthService authService, MailService mailService)
            : base(dbService, authService)
        {
            _mailService = mailService;
        }

        public class LoginRequest
        {
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
        }

        [HttpPost("login")]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
        public IActionResult Login([FromBody] LoginRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                {
                    return BadRequest(new { error = "E-posta ve şifre gereklidir." });
                }

                var data = Db.ReadDb();
                var user = data.Users.FirstOrDefault(u => string.Equals(u.Email.Trim(), req.Email.Trim(), StringComparison.OrdinalIgnoreCase));

                if (user == null || !Auth.ComparePassword(req.Password, user.Password))
                {
                    return Unauthorized(new { error = "E-posta adresi veya şifre hatalı!" });
                }

                var token = Auth.CreateSession(user.Email, user.Role);
                return Ok(new { success = true, token, email = user.Email, role = user.Role });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Login failed: {ex}");
                return StatusCode(500, new { error = "Giriş işlemi sırasında hata oluştu." });
            }
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            var authCheck = CheckAuth();
            if (authCheck != null) return authCheck;

            var authHeader = Request.Headers["Authorization"].ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader.Substring(7).Trim();
                Auth.DeleteSession(token);
            }

            return Ok(new { success = true });
        }

        [HttpGet("me")]
        public IActionResult Me()
        {
            var authCheck = CheckAuth();
            if (authCheck != null) return authCheck;

            return Ok(new { success = true, email = CurrentUserEmail, role = CurrentUserRole });
        }

        public class ChangePasswordRequest
        {
            public string OldPassword { get; set; } = "";
            public string NewPassword { get; set; } = "";
        }

        [HttpPost("change-password")]
        public IActionResult ChangePassword([FromBody] ChangePasswordRequest req)
        {
            var authCheck = CheckAuth();
            if (authCheck != null) return authCheck;

            try
            {
                if (string.IsNullOrWhiteSpace(req.OldPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
                {
                    return BadRequest(new { error = "Eski ve yeni şifre gereklidir." });
                }

                var passwordError = ValidatePasswordStrength(req.NewPassword);
                if (passwordError != null)
                {
                    return BadRequest(new { error = passwordError });
                }

                bool notFound = false;
                bool wrongOld = false;
                string? userEmail = null;

                var ok = Db.Update(data =>
                {
                    var user = data.Users.FirstOrDefault(u => string.Equals(u.Email, CurrentUserEmail, StringComparison.OrdinalIgnoreCase));
                    if (user == null) { notFound = true; return false; }
                    if (!Auth.ComparePassword(req.OldPassword, user.Password)) { wrongOld = true; return false; }

                    user.Password = Auth.HashPassword(req.NewPassword);
                    userEmail = user.Email;
                    return true;
                });

                if (notFound) return NotFound(new { error = "Kullanıcı bulunamadı." });
                if (wrongOld) return BadRequest(new { error = "Eski şifreniz hatalı!" });
                if (!ok) return StatusCode(500, new { error = "Şifre güncellenirken hata oluştu." });

                // Invalidate the user's other sessions but keep the current one alive,
                // so changing your own password doesn't immediately log you out.
                Auth.DeleteAllSessionsForUser(userEmail!, CurrentToken);

                return Ok(new { success = true, message = "Şifreniz başarıyla güncellendi." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] ChangePassword failed: {ex}");
                return StatusCode(500, new { error = "Şifre güncellenirken hata oluştu." });
            }
        }

        public class ForgotPasswordRequest
        {
            public string Email { get; set; } = "";
        }

        [HttpPost("forgot-password")]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.Email))
                {
                    return BadRequest(new { error = "E-posta adresi gereklidir." });
                }

                // Always return the same generic response regardless of whether the
                // email exists or whether SMTP is configured, so this endpoint can't
                // be used to enumerate registered accounts.
                var successMessage = "Eğer bu e-posta adresi sistemde kayıtlıysa, şifre sıfırlama bağlantısı gönderilecektir.";

                var data = Db.ReadDb();
                var settings = data.Settings;
                var user = data.Users.FirstOrDefault(u => string.Equals(u.Email.Trim(), req.Email.Trim(), StringComparison.OrdinalIgnoreCase));

                if (user != null)
                {
                    if (settings == null || string.IsNullOrWhiteSpace(settings.SmtpHost) || string.IsNullOrWhiteSpace(settings.SmtpUser))
                    {
                        // Log for the admin, but do not reveal anything to the caller.
                        Console.WriteLine("[Auth] Password reset requested but SMTP is not configured; skipping email.");
                    }
                    else
                    {
                        byte[] randomBytes = new byte[20];
                        RandomNumberGenerator.Fill(randomBytes);
                        var resetToken = Convert.ToHexString(randomBytes).ToLowerInvariant();
                        var expiry = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();

                        // Persist only the hash; the raw token travels in the email link.
                        bool tokenSet = false;
                        var persisted = Db.Update(d =>
                        {
                            var u = d.Users.FirstOrDefault(x => string.Equals(x.Email, user.Email, StringComparison.OrdinalIgnoreCase));
                            if (u == null) return false;
                            u.ResetToken = AuthService.HashToken(resetToken);
                            u.ResetTokenExpiry = expiry;
                            tokenSet = true;
                            return true;
                        });

                        // No point emailing a token we failed to persist (reset would just fail).
                        // tokenSet => the user was found and mutated; persisted => the write succeeded.
                        if (!(tokenSet && persisted))
                        {
                            Console.WriteLine("[Auth] Could not persist reset token; skipping email.");
                        }
                        else
                        {
                            string? origin = Request.Headers["Origin"];
                            if (string.IsNullOrEmpty(origin))
                            {
                                origin = Request.Headers["Referer"];
                                if (!string.IsNullOrEmpty(origin))
                                {
                                    var uri = new Uri(origin);
                                    origin = $"{uri.Scheme}://{uri.Authority}";
                                }
                            }

                            try
                            {
                                await _mailService.SendPasswordResetEmailAsync(user.Email, resetToken, settings, origin);
                            }
                            catch (Exception mailEx)
                            {
                                Console.WriteLine($"[Auth] Failed to send reset email: {mailEx.Message}");
                            }
                        }
                    }
                }

                return Ok(new { success = true, message = successMessage });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] ForgotPassword failed: {ex}");
                // Still return generic success to avoid leaking internal state.
                return Ok(new { success = true, message = "Eğer bu e-posta adresi sistemde kayıtlıysa, şifre sıfırlama bağlantısı gönderilecektir." });
            }
        }

        public class ResetPasswordRequest
        {
            public string Token { get; set; } = "";
            public string Password { get; set; } = "";
        }

        [HttpPost("reset-password")]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
        public IActionResult ResetPassword([FromBody] ResetPasswordRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.Password))
                {
                    return BadRequest(new { error = "Geçersiz istek. Sıfırlama kodu ve şifre gereklidir." });
                }

                var passwordError = ValidatePasswordStrength(req.Password);
                if (passwordError != null)
                {
                    return BadRequest(new { error = passwordError });
                }

                var hashedToken = AuthService.HashToken(req.Token);
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                bool invalid = false;
                string? userEmail = null;

                var ok = Db.Update(data =>
                {
                    var user = data.Users.FirstOrDefault(u =>
                        u.ResetToken == hashedToken &&
                        u.ResetTokenExpiry.HasValue &&
                        u.ResetTokenExpiry.Value > nowMs);

                    if (user == null) { invalid = true; return false; }

                    user.Password = Auth.HashPassword(req.Password);
                    user.ResetToken = null;
                    user.ResetTokenExpiry = null;
                    userEmail = user.Email;
                    return true;
                });

                if (invalid)
                {
                    return BadRequest(new { error = "Geçersiz veya süresi dolmuş sıfırlama bağlantısı!" });
                }
                if (!ok) return StatusCode(500, new { error = "Şifre yenilenirken hata oluştu." });

                // Invalidate all other sessions for this user
                Auth.DeleteAllSessionsForUser(userEmail!);

                return Ok(new { success = true, message = "Şifreniz başarıyla yenilendi! Yeni şifrenizle giriş yapabilirsiniz." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] ResetPassword failed: {ex}");
                return StatusCode(500, new { error = "Şifre yenilenirken hata oluştu." });
            }
        }
    }
}
