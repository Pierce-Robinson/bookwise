using System.ComponentModel.DataAnnotations;

namespace Bookwise.Api.Infrastructure;

public class BusinessOptions
{
    public const string Section = "Business";

    [Required]
    public string Name { get; set; } = "Bookwise Studio";

    /// <summary>IANA time zone, for example Africa/Johannesburg. Working hours are in this zone.</summary>
    [Required]
    public string TimeZoneId { get; set; } = "Africa/Johannesburg";

    [Range(5, 60)]
    public int SlotIntervalMinutes { get; set; } = 15;

    [Range(0, 10080)]
    public int MinimumNoticeMinutes { get; set; } = 60;

    [Range(1, 365)]
    public int MaxDaysAhead { get; set; } = 60;

    [Range(0, 168)]
    public int CancellationCutoffHours { get; set; } = 24;

    [Range(1, 168)]
    public int ReminderLeadHours { get; set; } = 24;

    [Range(10, 86400)]
    public int ReminderCheckIntervalSeconds { get; set; } = 300;

    [Range(1, 100000)]
    public int PublicRateLimitPerMinute { get; set; } = 20;

    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}

public class JwtOptions
{
    public const string Section = "Jwt";

    [Required, MinLength(32)]
    public string Key { get; set; } = "";

    [Required]
    public string Issuer { get; set; } = "bookwise";

    [Required]
    public string Audience { get; set; } = "bookwise-admin";

    [Range(1, 1440)]
    public int ExpiryMinutes { get; set; } = 60;
}

public class SeedOptions
{
    public const string Section = "Seed";

    public bool DemoData { get; set; }
    public string AdminEmail { get; set; } = "admin@bookwise.local";
    public string AdminPassword { get; set; } = "";
}
