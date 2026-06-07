using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using SigortaTakip.Models;
using SigortaTakip.Services;

namespace SigortaTakip.Controllers
{
    public class BaseApiController : ControllerBase
    {
        protected readonly DbService Db;
        protected readonly AuthService Auth;

        // Reasonable upper bounds to stop oversized strings being persisted.
        protected const int MaxPlateLength = 20;
        protected const int MaxTextLength = 120;

        protected static readonly string[] PolicyKeys = { "trafik", "kasko", "koltuk" };

        public BaseApiController(DbService dbService, AuthService authService)
        {
            Db = dbService;
            Auth = authService;
        }

        protected SessionData? CurrentSession => HttpContext.Items["Session"] as SessionData;
        protected string? CurrentToken => HttpContext.Items["Token"] as string;

        protected string? CurrentUserEmail => CurrentSession?.Email;
        protected string? CurrentUserRole => CurrentSession?.Role;

        protected bool IsSuperAdmin => string.Equals(CurrentUserRole, "superadmin", StringComparison.OrdinalIgnoreCase);

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
            if (!password.Any(char.IsUpper))
            {
                return "Şifre en az bir büyük harf içermelidir.";
            }
            if (!password.Any(char.IsLower))
            {
                return "Şifre en az bir küçük harf içermelidir.";
            }
            if (!password.Any(char.IsDigit))
            {
                return "Şifre en az bir rakam içermelidir.";
            }
            return null;
        }

        protected static bool IsValidDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return false;
            if (!Regex.IsMatch(dateStr, @"^\d{4}-\d{2}-\d{2}$")) return false;
            return DateTime.TryParseExact(dateStr, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }

        /// <summary>
        /// Validates the core bus fields (lengths) and the three required policies'
        /// date formats / ordering. Returns an error message, or null if valid.
        /// </summary>
        protected static string? ValidateBus(string? plate, string? brand, string? @operator, Dictionary<string, Policy>? policies)
        {
            if (string.IsNullOrWhiteSpace(plate) || string.IsNullOrWhiteSpace(brand) ||
                string.IsNullOrWhiteSpace(@operator) || policies == null)
            {
                return "Tüm alanlar zorunludur.";
            }

            if (plate.Length > MaxPlateLength) return "Plaka çok uzun.";
            if (brand.Length > MaxTextLength) return "Marka / Model çok uzun.";
            if (@operator.Length > MaxTextLength) return "Firma / Acente adı çok uzun.";

            foreach (var key in PolicyKeys)
            {
                if (!policies.TryGetValue(key, out var policy) || policy == null ||
                    !IsValidDate(policy.StartDate) || !IsValidDate(policy.EndDate))
                {
                    return $"Geçersiz tarih formatı: {key}";
                }
                if (string.CompareOrdinal(policy.EndDate, policy.StartDate) < 0)
                {
                    return $"{key}: bitiş tarihi başlangıç tarihinden önce olamaz.";
                }
            }

            return null;
        }
    }
}
