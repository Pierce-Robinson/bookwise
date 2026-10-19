using System.Collections.Concurrent;
using Bookwise.Api.Domain;
using Bookwise.Api.Features.Reminders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Bookwise.Tests.Support;

/// <summary>
/// Hosts the real app against its own SQLite database file, with a controllable clock and a
/// notification sender that records instead of sending.
/// </summary>
public class BookwiseFactory : WebApplicationFactory<Program>
{
    /// <summary>Monday 7 January 2030, 08:00 in Johannesburg (06:00 UTC).</summary>
    public static readonly DateTimeOffset StartTime = new(2030, 1, 7, 6, 0, 0, TimeSpan.Zero);

    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin123!";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"bookwise-test-{Guid.NewGuid():N}.db");

    public FakeTimeProvider Clock { get; } = new(StartTime);

    public RecordingNotificationSender Notifications { get; } = new();

    protected virtual int RateLimitPerMinute => 100_000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Bookwise"] = $"Data Source={_dbPath}",
            ["Jwt:Key"] = "integration-test-signing-key-0123456789abcdef",
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = AdminPassword,
            ["Seed:DemoData"] = "false",
            ["Business:PublicRateLimitPerMinute"] = RateLimitPerMinute.ToString(),
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<INotificationSender>();
            services.AddSingleton<INotificationSender>(Notifications);
            // Tests run the reminder processor directly instead of on a background timer.
            var reminderService = services.Single(d => d.ImplementationType == typeof(ReminderBackgroundService));
            services.Remove(reminderService);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best effort; it's a temp file.
        }
    }
}

public sealed class RecordingNotificationSender : INotificationSender
{
    public ConcurrentBag<string> SentReferences { get; } = [];

    public Task SendReminderAsync(Booking booking, DateTimeOffset startLocal, CancellationToken ct)
    {
        SentReferences.Add(booking.Reference);
        return Task.CompletedTask;
    }
}
