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

                bool duplicate = false;
                User? newUser = null;

                // Duplicate-email check + insert atomically.
                Db.Update(data =>
                {
                    if (data.Users.Any(u => string.Equals(u.Email.Trim(), req.Email.Trim(), StringComparison.OrdinalIgnoreCase)))
                    {
                        duplicate = true;
                        return false;
                    }

                    newUser = new User
                    {
                        Id = "user-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Email = req.Email.ToLowerInvariant().Trim(),
                        Password = Auth.HashPassword(req.Password),
                        Role = role,
                        ResetToken = null,
                        ResetTokenExpiry = null
                    };

                    data.Users.Add(newUser);
                    return true;
                });

                if (duplicate) return BadRequest(new { error = "Bu e-posta adresi zaten yetkilendirilmiş!" });
                return StatusCode(201, new { id = newUser!.Id, email = newUser.Email, role = newUser.Role });
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

                bool notFound = false;
                string? affectedEmail = null;

                Db.Update(data =>
                {
                    var user = data.Users.FirstOrDefault(u => string.Equals(u.Email, targetEmail, StringComparison.OrdinalIgnoreCase));
                    if (user == null) { notFound = true; return false; }

                    user.Role = role;
                    affectedEmail = user.Email;
                    return true;
                });

                if (notFound) return NotFound(new { error = "Kullanıcı bulunamadı." });

                // Force the affected user to re-authenticate so the new role takes effect.
                Auth.DeleteAllSessionsForUser(affectedEmail!);

                return Ok(new { success = true, email = affectedEmail, role });
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

                bool notFound = false;
                bool lastAdmin = false;

                // Find + last-superadmin check + remove atomically, so a concurrent
                // delete/demote can't drop the system below one superadmin.
                Db.Update(data =>
                {
                    var userIndex = data.Users.FindIndex(u => string.Equals(u.Email, targetEmail, StringComparison.OrdinalIgnoreCase));
                    if (userIndex == -1) { notFound = true; return false; }

                    var target = data.Users[userIndex];
                    if (string.Equals(target.Role, "superadmin", StringComparison.OrdinalIgnoreCase) &&
                        data.Users.Count(u => string.Equals(u.Role, "superadmin", StringComparison.OrdinalIgnoreCase)) <= 1)
                    {
                        lastAdmin = true;
                        return false;
                    }

                    data.Users.RemoveAt(userIndex);
                    return true;
                });

                if (notFound) return NotFound(new { error = "Kullanıcı bulunamadı." });
                if (lastAdmin) return BadRequest(new { error = "Sistemde en az bir yönetici bulunmalıdır." });

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
