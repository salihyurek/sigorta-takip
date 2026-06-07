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
        public UsersController(DbService dbService, AuthService authService)
            : base(dbService, authService)
        {
        }

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
                    role = u.Role
                }).ToList();

                return Ok(usersList);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Kullanıcılar listelenemedi", details = ex.Message });
            }
        }

        public class CreateUserRequest
        {
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
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

                var passwordError = ValidatePasswordStrength(req.Password);
                if (passwordError != null)
                {
                    return BadRequest(new { error = passwordError });
                }

                var data = Db.ReadDb();
                var emailExists = data.Users.Any(u => string.Equals(u.Email.Trim(), req.Email.Trim(), StringComparison.OrdinalIgnoreCase));

                if (emailExists)
                {
                    return BadRequest(new { error = "Bu e-posta adresi zaten yetkilendirilmiş!" });
                }

                var newUser = new User
                {
                    Id = "user-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Email = req.Email.ToLowerInvariant().Trim(),
                    Password = Auth.HashPassword(req.Password),
                    Role = "viewer", // Added users default to viewers
                    ResetToken = null,
                    ResetTokenExpiry = null
                };

                data.Users.Add(newUser);
                Db.WriteDb(data);

                return StatusCode(201, new { id = newUser.Id, email = newUser.Email });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Kullanıcı eklenemedi", details = ex.Message });
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

                var data = Db.ReadDb();

                if (data.Users.Count <= 1)
                {
                    return BadRequest(new { error = "Sistemde en az bir yönetici bulunmalıdır." });
                }

                var userIndex = data.Users.FindIndex(u => string.Equals(u.Email, targetEmail, StringComparison.OrdinalIgnoreCase));
                if (userIndex == -1)
                {
                    return NotFound(new { error = "Kullanıcı bulunamadı." });
                }

                data.Users.RemoveAt(userIndex);
                Db.WriteDb(data);

                return Ok(new { success = true, message = "Kullanıcı yetkisi kaldırıldı." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Kullanıcı silinemedi", details = ex.Message });
            }
        }
    }
}
