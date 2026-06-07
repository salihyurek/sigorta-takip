using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using SigortaTakip.Models;
using SigortaTakip.Services;

namespace SigortaTakip.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : BaseApiController
    {
        private static readonly string[] AllowedRoles = { "viewer", "superadmin" };

        public UsersController(DbService dbService, AuthService authService)
            : base(dbService, authService)
        {
        }

        private bool IsBootstrapAdmin(string email) =>
            string.Equals(email.Trim(), Db.SuperadminEmail.Trim(), StringComparison.OrdinalIgnoreCase);

        [HttpGet]
        public IActionResult GetUsers()
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                var data = Db.ReadDb();
                var usersList = data.Users.Select(u => new
                {
                    id = u.Id,
                    email = u.Email,
                    role = u.Role,
                    isBootstrap = IsBootstrapAdmin(u.Email)
                }).ToList();

                return Ok(usersList);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Users] GetUsers failed: {ex}");
                return StatusCode(500, new { error = "Kullanıcılar listelenemedi." });
            }
        }

        public class CreateUserRequest
        {
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
            public string? Role { get; set; }
        }

        [HttpPost]
        public IActionResult CreateUser([FromBody] CreateUserRequest req)
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                {
                    return BadRequest(new { error = "E-posta ve şifre gereklidir." });
                }
                if (req.Email.Length > MaxTextLength)
                {
                    return BadRequest(new { error = "E-posta adresi çok uzun." });
                }

                var role = string.IsNullOrWhiteSpace(req.Role) ? "viewer" : req.Role.Trim().ToLowerInvariant();
                if (!AllowedRoles.Contains(role))
                {
                    return BadRequest(new { error = "Geçersiz yetki seçimi." });
                }

                var passwordError = ValidatePasswordStrength(req.Password);
                if (passwordError != null) return BadRequest(new { error = passwordError });

                var data = Db.ReadDb();
                if (data.Users.Any(u => string.Equals(u.Email.Trim(), req.Email.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    return BadRequest(new { error = "Bu e-posta adresi zaten yetkilendirilmiş!" });
                }

                var newUser = new User
                {
                    Id = "user-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Email = req.Email.ToLowerInvariant().Trim(),
                    Password = Auth.HashPassword(req.Password),
                    Role = role,
                    ResetToken = null,
                    ResetTokenExpiry = null
                };

                data.Users.Add(newUser);
                Db.WriteDb(data);

                return StatusCode(201, new { id = newUser.Id, email = newUser.Email, role = newUser.Role });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Users] CreateUser failed: {ex}");
                return StatusCode(500, new { error = "Kullanıcı eklenemedi." });
            }
        }

        public class UpdateRoleRequest
        {
            public string Role { get; set; } = "";
        }

        [HttpPut("{email}/role")]
        public IActionResult UpdateRole(string email, [FromBody] UpdateRoleRequest req)
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                var role = (req.Role ?? "").Trim().ToLowerInvariant();
                if (!AllowedRoles.Contains(role))
                {
                    return BadRequest(new { error = "Geçersiz yetki seçimi." });
                }

                var targetEmail = email.ToLowerInvariant().Trim();

                if (IsBootstrapAdmin(targetEmail))
                {
                    return BadRequest(new { error = "Ana yönetici hesabının yetkisi değiştirilemez." });
                }
                if (string.Equals(targetEmail, CurrentUserEmail?.ToLowerInvariant().Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { error = "Kendi yetkinizi değiştiremezsiniz." });
                }

                var data = Db.ReadDb();
                var user = data.Users.FirstOrDefault(u => string.Equals(u.Email, targetEmail, StringComparison.OrdinalIgnoreCase));
                if (user == null) return NotFound(new { error = "Kullanıcı bulunamadı." });

                user.Role = role;
                Db.WriteDb(data);

                // Force the affected user to re-authenticate so the new role takes effect.
                Auth.DeleteAllSessionsForUser(user.Email);

                return Ok(new { success = true, email = user.Email, role = user.Role });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Users] UpdateRole failed: {ex}");
                return StatusCode(500, new { error = "Yetki güncellenemedi." });
            }
        }

        [HttpDelete("{email}")]
        public IActionResult DeleteUser(string email)
        {
            var adminCheck = CheckSuperAdmin();
            if (adminCheck != null) return adminCheck;

            try
            {
                var targetEmail = email.ToLowerInvariant().Trim();

                if (string.Equals(targetEmail, CurrentUserEmail?.ToLowerInvariant().Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { error = "Kendi hesabınızı silemezsiniz!" });
                }
                if (IsBootstrapAdmin(targetEmail))
                {
                    return BadRequest(new { error = "Ana yönetici hesabı silinemez." });
                }

                var data = Db.ReadDb();
                var userIndex = data.Users.FindIndex(u => string.Equals(u.Email, targetEmail, StringComparison.OrdinalIgnoreCase));
                if (userIndex == -1) return NotFound(new { error = "Kullanıcı bulunamadı." });

                // Never leave the system without at least one superadmin.
                var target = data.Users[userIndex];
                if (string.Equals(target.Role, "superadmin", StringComparison.OrdinalIgnoreCase) &&
                    data.Users.Count(u => string.Equals(u.Role, "superadmin", StringComparison.OrdinalIgnoreCase)) <= 1)
                {
                    return BadRequest(new { error = "Sistemde en az bir yönetici bulunmalıdır." });
                }

                data.Users.RemoveAt(userIndex);
                Db.WriteDb(data);

                Auth.DeleteAllSessionsForUser(targetEmail);

                return Ok(new { success = true, message = "Kullanıcı yetkisi kaldırıldı." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Users] DeleteUser failed: {ex}");
                return StatusCode(500, new { error = "Kullanıcı silinemedi." });
            }
        }
    }
}
