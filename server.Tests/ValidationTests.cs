using System.Collections.Generic;
using SigortaTakip.Controllers;
using SigortaTakip.Models;

namespace SigortaTakip.Tests
{
    public class ValidationTests
    {
        private static Dictionary<string, Policy> ValidPolicies() => new()
        {
            { "trafik", new Policy { StartDate = "2026-01-01", EndDate = "2027-01-01" } },
            { "kasko",  new Policy { StartDate = "2026-01-01", EndDate = "2027-01-01" } },
            { "koltuk", new Policy { StartDate = "2026-01-01", EndDate = "2027-01-01" } },
        };

        [Fact]
        public void ValidateBus_AcceptsAWellFormedBus()
        {
            Assert.Null(BaseApiController.ValidateBus("34 ABC 123", "Mercedes Travego", "Metro Turizm", ValidPolicies()));
        }

        [Fact]
        public void ValidateBus_RejectsMissingCoreFields()
        {
            Assert.Equal("Tüm alanlar zorunludur.",
                BaseApiController.ValidateBus("34 ABC 123", "", "Metro", ValidPolicies()));
            Assert.Equal("Tüm alanlar zorunludur.",
                BaseApiController.ValidateBus("34 ABC 123", "Marka", "Firma", null));
        }

        [Fact]
        public void ValidateBus_RejectsOverlongPlate()
        {
            Assert.Equal("Plaka çok uzun.",
                BaseApiController.ValidateBus(new string('A', 21), "Marka", "Firma", ValidPolicies()));
        }

        [Fact]
        public void ValidateBus_RejectsBadDateFormat()
        {
            var policies = ValidPolicies();
            policies["kasko"].EndDate = "2027/01/01"; // wrong separator
            Assert.Equal("Geçersiz tarih formatı: kasko",
                BaseApiController.ValidateBus("34 ABC 123", "Marka", "Firma", policies));
        }

        [Fact]
        public void ValidateBus_RejectsEndBeforeStart()
        {
            var policies = ValidPolicies();
            policies["trafik"].StartDate = "2027-01-01";
            policies["trafik"].EndDate = "2026-01-01";
            Assert.Equal("trafik: bitiş tarihi başlangıç tarihinden önce olamaz.",
                BaseApiController.ValidateBus("34 ABC 123", "Marka", "Firma", policies));
        }

        [Fact]
        public void ValidateBus_AllowsSameStartAndEndDate()
        {
            var policies = ValidPolicies();
            policies["koltuk"].StartDate = "2026-06-07";
            policies["koltuk"].EndDate = "2026-06-07";
            Assert.Null(BaseApiController.ValidateBus("34 ABC 123", "Marka", "Firma", policies));
        }

        [Theory]
        [InlineData("2026-06-07", true)]
        [InlineData("2026-13-01", false)] // invalid month
        [InlineData("2026-02-30", false)] // invalid day
        [InlineData("2026-6-7", false)]   // not zero-padded
        [InlineData("", false)]
        public void IsValidDate_ChecksFormatAndCalendar(string input, bool expected)
        {
            Assert.Equal(expected, BaseApiController.IsValidDate(input));
        }

        [Fact]
        public void ValidatePasswordStrength_AcceptsAStrongPassword()
        {
            Assert.Null(BaseApiController.ValidatePasswordStrength("Sifre123"));
        }

        [Theory]
        [InlineData("Ab1")]        // too short
        [InlineData("sifre123")]   // no uppercase
        [InlineData("SIFRE123")]   // no lowercase
        [InlineData("SifreABC")]   // no digit
        public void ValidatePasswordStrength_RejectsWeakPasswords(string password)
        {
            Assert.NotNull(BaseApiController.ValidatePasswordStrength(password));
        }

        [Fact]
        public void ValidatePasswordStrength_RejectsPasswordsOver72Bytes()
        {
            // 72 bytes is the BCrypt limit; longer must be rejected, not silently truncated.
            var ok72 = "Aa1" + new string('x', 69);   // exactly 72 ASCII bytes
            var tooLong = "Aa1" + new string('x', 70); // 73 bytes
            Assert.Null(BaseApiController.ValidatePasswordStrength(ok72));
            Assert.NotNull(BaseApiController.ValidatePasswordStrength(tooLong));
        }

        [Theory]
        [InlineData("user@example.com", true)]
        [InlineData("a.b-c@sub.domain.co", true)]
        [InlineData("no-at-sign", false)]
        [InlineData("no@domain", false)]   // no dot after @
        [InlineData("two @spaces.com", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsValidEmail_ChecksBasicShape(string? email, bool expected)
        {
            Assert.Equal(expected, BaseApiController.IsValidEmail(email));
        }
    }
}
