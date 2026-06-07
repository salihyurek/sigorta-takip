using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SigortaTakip.Models
{
    public class Settings
    {
        [JsonPropertyName("smtpHost")]
        public string SmtpHost { get; set; } = "";

        [JsonPropertyName("smtpPort")]
        public int SmtpPort { get; set; } = 587;

        [JsonPropertyName("smtpUser")]
        public string SmtpUser { get; set; } = "";

        [JsonPropertyName("smtpPass")]
        public string SmtpPass { get; set; } = "";

        [JsonPropertyName("senderName")]
        public string SenderName { get; set; } = "";

        [JsonPropertyName("senderEmail")]
        public string SenderEmail { get; set; } = "";

        [JsonPropertyName("receiverEmail")]
        public string ReceiverEmail { get; set; } = "";

        [JsonPropertyName("enableEmails")]
        public bool EnableEmails { get; set; } = false;
    }

    public class Policy
    {
        [JsonPropertyName("startDate")]
        public string StartDate { get; set; } = "";

        [JsonPropertyName("endDate")]
        public string EndDate { get; set; } = "";

        [JsonPropertyName("lastEmailedDate")]
        public string? LastEmailedDate { get; set; }
    }

    public class Bus
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("plate")]
        public string Plate { get; set; } = "";

        [JsonPropertyName("brand")]
        public string Brand { get; set; } = "";

        [JsonPropertyName("operator")]
        public string Operator { get; set; } = "";

        [JsonPropertyName("policies")]
        public Dictionary<string, Policy> Policies { get; set; } = new();
    }

    public class User
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("password")]
        public string Password { get; set; } = "";

        [JsonPropertyName("role")]
        public string Role { get; set; } = "viewer";

        [JsonPropertyName("resetToken")]
        public string? ResetToken { get; set; }

        [JsonPropertyName("resetTokenExpiry")]
        public long? ResetTokenExpiry { get; set; }
    }

    public class DatabaseData
    {
        [JsonPropertyName("settings")]
        public Settings Settings { get; set; } = new();

        [JsonPropertyName("buses")]
        public List<Bus> Buses { get; set; } = new();

        [JsonPropertyName("users")]
        public List<User> Users { get; set; } = new();
    }
}
