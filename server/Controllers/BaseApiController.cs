using Microsoft.AspNetCore.Mvc;
using SigortaTakip.Services;

namespace SigortaTakip.Controllers
{
    public class BaseApiController : ControllerBase
    {
        protected readonly DbService Db;
        protected readonly AuthService Auth;

        public BaseApiController(DbService dbService, AuthService authService)
        {
            Db = dbService;
            Auth = authService;
        }

        protected SessionData? CurrentSession => HttpContext.Items["Session"] as SessionData;

        protected string? CurrentUserEmail => CurrentSession?.Email;
        protected string? CurrentUserRole => CurrentSession?.Role;

        protected bool IsSuperAdmin => string.Equals(CurrentUserRole, "superadmin", System.StringComparison.OrdinalIgnoreCase);

        protected IActionResult? CheckAuth()
        {
            if (CurrentSession == null)
            {
                return Unauthorized(new { error = "Giriş yapmanız gerekmektedir." });
            }
            return null;
        }

        protected IActionResult? CheckSuperAdmin()
        {
            var authCheck = CheckAuth();
            if (authCheck != null) return authCheck;

            if (!IsSuperAdmin)
            {
                return StatusCode(403, new { error = "Bu işlem için yetkiniz bulunmamaktadır." });
            }
            return null;
        }

        protected string? ValidatePasswordStrength(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 8)
            {
                return "Şifre en az 8 karakter uzunluğunda olmalıdır.";
            }
            if (!System.Linq.Enumerable.Any(password, char.IsUpper))
            {
                return "Şifre en az bir büyük harf içermelidir.";
            }
            if (!System.Linq.Enumerable.Any(password, char.IsLower))
            {
                return "Şifre en az bir küçük harf içermelidir.";
            }
            if (!System.Linq.Enumerable.Any(password, char.IsDigit))
            {
                return "Şifre en az bir rakam içermelidir.";
            }
            return null;
        }
    }
}
