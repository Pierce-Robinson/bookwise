using Bookwise.Api.Features.Reminders;
using Bookwise.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Bookwise.Tests;

/// <summary>Uses its own app instance because it moves the clock.</summary>
public class ReminderTests(BookwiseFactory factory) : IClassFixture<BookwiseFactory>
{
    private readonly TestApi _api = new(factory);

    private async Task<int> RunRemindersAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ReminderProcessor>().ProcessDueAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Each_customer_is_reminded_once_within_24_hours_and_cancelled_bookings_are_skipped()
    {
        var service = await _api.CreateServiceAsync();
        await _api.CreateStaffAsync(service);
        var kept = await _api.BookOkAsync(service, TestApi.Sast(TestApi.Tuesday, 10));      // 26 hours away
        var cancelled = await _api.BookOkAsync(service, TestApi.Sast(TestApi.Tuesday, 11)); // 27 hours away
        var admin = await _api.AdminAsync();
        (await admin.PostAsync($"/api/admin/bookings/{cancelled.Booking.Id}/cancel", null)).EnsureSuccessStatusCode();

        Assert.Equal(0, await RunRemindersAsync()); // too early

        factory.Clock.Advance(TimeSpan.FromHours(3)); // now 23 hours before the kept booking
        Assert.Equal(1, await RunRemindersAsync());
        Assert.Equal(0, await RunRemindersAsync()); // nothing new the second time

        Assert.Equal([kept.Reference], factory.Notifications.SentReferences);
    }
}
