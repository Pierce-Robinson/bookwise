using Bookwise.Api.Data;
using Bookwise.Api.Domain;
using Bookwise.Api.Features.Availability;
using Bookwise.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bookwise.Api.Features.Reminders;

/// <summary>Sends customer notifications. Swap the logging implementation for email or SMS in production.</summary>
public interface INotificationSender
{
    Task SendReminderAsync(Booking booking, DateTimeOffset startLocal, CancellationToken ct);
}

public sealed class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    public Task SendReminderAsync(Booking booking, DateTimeOffset startLocal, CancellationToken ct)
    {
        logger.LogInformation("Reminder to {Email}: {Service} with {Staff} at {Start:ddd d MMM HH:mm} (ref {Reference})",
            booking.CustomerEmail, booking.Service.Name, booking.StaffMember.Name, startLocal, booking.Reference);
        return Task.CompletedTask;
    }
}

/// <summary>Finds confirmed bookings starting within the reminder window and reminds each customer once.</summary>
public class ReminderProcessor(
    BookwiseDbContext db,
    INotificationSender sender,
    IOptions<BusinessOptions> options,
    TimeProvider clock,
    ILogger<ReminderProcessor> logger)
{
    public async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        var business = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var horizon = now.AddHours(business.ReminderLeadHours);

        var due = await db.Bookings
            .Include(b => b.Service)
            .Include(b => b.StaffMember)
            .Where(b => b.Status == BookingStatus.Confirmed && b.ReminderSentUtc == null
                        && b.StartUtc > now && b.StartUtc <= horizon)
            .OrderBy(b => b.StartUtc)
            .Take(100)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var booking in due)
        {
            try
            {
                await sender.SendReminderAsync(booking, AvailabilityService.ToLocal(booking.StartUtc, business.TimeZone), ct);
                booking.ReminderSentUtc = now;
                await db.SaveChangesAsync(ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One failed send shouldn't block the rest; it will be retried on the next run.
                logger.LogWarning(ex, "Could not send reminder for booking {Reference}", booking.Reference);
            }
        }
        return sent;
    }
}

/// <summary>Runs <see cref="ReminderProcessor"/> on a timer for the lifetime of the app.</summary>
public sealed class ReminderBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<BusinessOptions> options,
    TimeProvider clock,
    ILogger<ReminderBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.ReminderCheckIntervalSeconds), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sent = await scope.ServiceProvider.GetRequiredService<ReminderProcessor>().ProcessDueAsync(stoppingToken);
                if (sent > 0)
                {
                    logger.LogInformation("Sent {Count} booking reminders", sent);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reminder run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
