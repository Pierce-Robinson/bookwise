using System.Net;
using System.Net.Http.Json;
using Bookwise.Api.Features.Admin;
using Bookwise.Api.Features.Bookings;
using Bookwise.Tests.Support;

namespace Bookwise.Tests;

public class BookingFlowTests(BookwiseFactory factory) : IClassFixture<BookwiseFactory>
{
    private readonly TestApi _api = new(factory);
    private static readonly DateOnly Tuesday = TestApi.Tuesday;

    [Fact]
    public async Task Availability_lists_slots_within_working_hours_in_local_time()
    {
        var service = await _api.CreateServiceAsync(durationMinutes: 60);
        var staff = await _api.CreateStaffAsync(service);

        var availability = await _api.AvailabilityAsync(service, Tuesday);

        Assert.Equal("Africa/Johannesburg", availability.TimeZone);
        Assert.Equal(TestApi.Sast(Tuesday, 9), availability.Slots[0].StartUtc);
        Assert.Equal(TestApi.Sast(Tuesday, 16), availability.Slots[^1].StartUtc);
        Assert.Equal(TimeSpan.FromHours(2), availability.Slots[0].StartLocal.Offset);
        Assert.Equal(9, availability.Slots[0].StartLocal.Hour);
        Assert.All(availability.Slots, s => Assert.Equal([staff], s.StaffIds));
    }

    [Fact]
    public async Task Booking_a_slot_returns_a_reference_and_removes_the_slot()
    {
        var service = await _api.CreateServiceAsync();
        await _api.CreateStaffAsync(service);
        var start = TestApi.Sast(Tuesday, 10);

        var response = await _api.BookAsync(service, start);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<BookingCreatedResponse>(TestApi.Json))!;
        Assert.Matches("^BK-[A-Z2-9]{6}$", created.Reference);
        Assert.False(string.IsNullOrWhiteSpace(created.CancelToken));
        Assert.Equal(start, created.Booking.StartUtc);
        Assert.Equal(start.AddHours(1), created.Booking.EndUtc);
        Assert.Contains(created.Reference, response.Headers.Location!.ToString());

        var slots = (await _api.AvailabilityAsync(service, Tuesday)).Slots.Select(s => s.StartUtc).ToList();
        Assert.DoesNotContain(start, slots);
        Assert.DoesNotContain(start.AddMinutes(45), slots); // would overlap the new booking
        Assert.Contains(start.AddHours(1), slots);
    }

    [Fact]
    public async Task The_same_slot_cannot_be_booked_twice()
    {
        var service = await _api.CreateServiceAsync();
        await _api.CreateStaffAsync(service);
        var start = TestApi.Sast(Tuesday, 11);
        await _api.BookOkAsync(service, start);

        var second = await _api.BookAsync(service, start, email: "someone.else@example.com");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("SLOT_UNAVAILABLE", (await TestApi.ProblemAsync(second)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Concurrent_requests_for_one_slot_produce_exactly_one_booking()
    {
        var service = await _api.CreateServiceAsync();
        await _api.CreateStaffAsync(service);
        var start = TestApi.Sast(Tuesday, 12);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => _api.BookAsync(service, start, email: $"racer{i}@example.com")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Anyone_available_bookings_are_spread_across_staff()
    {
        var service = await _api.CreateServiceAsync();
        var first = await _api.CreateStaffAsync(service);
        var second = await _api.CreateStaffAsync(service);
        var start = TestApi.Sast(Tuesday, 13);

        var a = await _api.BookOkAsync(service, start);
        var b = await _api.BookOkAsync(service, start);
        var c = await _api.BookAsync(service, start);

        Assert.Equal([first, second], new[] { a.Booking.StaffId, b.Booking.StaffId }.Order());
        Assert.Equal(HttpStatusCode.Conflict, c.StatusCode);
    }

    [Theory]
    [InlineData(10, 7)]  // 10:07 is not on the 15-minute grid
    [InlineData(7, 0)]   // before opening
    [InlineData(16, 30)] // a 60-minute service would end after closing
    public async Task Times_that_were_never_offered_are_rejected(int hour, int minute)
    {
        var service = await _api.CreateServiceAsync();
        await _api.CreateStaffAsync(service);

        var response = await _api.BookAsync(service, TestApi.Sast(Tuesday, hour, minute));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_details_return_field_errors()
    {
        var response = await _api.Public.PostAsJsonAsync("/api/public/bookings", new
        {
            serviceId = 1,
            start = "2030-01-08T08:00:00Z",
            customerName = "J",
            customerEmail = "not-an-email",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await TestApi.ProblemAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("CustomerEmail", out _));
        Assert.True(errors.TryGetProperty("CustomerName", out _));
    }

    [Fact]
    public async Task Customers_can_view_and_cancel_with_their_private_link()
    {
        var service = await _api.CreateServiceAsync();
        await _api.CreateStaffAsync(service);
        var start = TestApi.Sast(Tuesday, 14);
        var booking = await _api.BookOkAsync(service, start);

        var view = await _api.Public.GetFromJsonAsync<BookingResponse>(
            $"/api/public/bookings/{booking.Reference}?token={booking.CancelToken}", TestApi.Json);
        Assert.Equal("Confirmed", view!.Status.ToString());

        var cancel = await _api.Public.PostAsJsonAsync($"/api/public/bookings/{booking.Reference}/cancel",
            new CancelBookingRequest(booking.CancelToken));
        cancel.EnsureSuccessStatusCode();
        Assert.Equal("Cancelled", (await cancel.Content.ReadFromJsonAsync<BookingResponse>(TestApi.Json))!.Status.ToString());

        var slots = (await _api.AvailabilityAsync(service, Tuesday)).Slots.Select(s => s.StartUtc);
        Assert.Contains(start, slots);
    }

    [Fact]
    public async Task A_wrong_token_looks_the_same_as_a_missing_booking()
    {
        var service = await _api.CreateServiceAsync();
        await _api.CreateStaffAsync(service);
        var booking = await _api.BookOkAsync(service, TestApi.Sast(Tuesday, 15));

        var wrongToken = await _api.Public.GetAsync($"/api/public/bookings/{booking.Reference}?token=guess");
        var noBooking = await _api.Public.GetAsync("/api/public/bookings/BK-ZZZZZZ?token=guess");

        Assert.Equal(HttpStatusCode.NotFound, wrongToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noBooking.StatusCode);
    }

    [Fact]
    public async Task Online_cancellation_closes_24_hours_before_the_appointment()
    {
        var service = await _api.CreateServiceAsync();
        await _api.CreateStaffAsync(service);
        var today = new DateOnly(2030, 1, 7); // the test clock is 08:00 on this day
        var booking = await _api.BookOkAsync(service, TestApi.Sast(today, 14));

        var cancel = await _api.Public.PostAsJsonAsync($"/api/public/bookings/{booking.Reference}/cancel",
            new CancelBookingRequest(booking.CancelToken));

        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Equal("CANCELLATION_WINDOW_PASSED", (await TestApi.ProblemAsync(cancel)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Time_off_blocks_availability()
    {
        var service = await _api.CreateServiceAsync();
        var staff = await _api.CreateStaffAsync(service);
        var admin = await _api.AdminAsync();

        var response = await admin.PostAsJsonAsync($"/api/admin/staff/{staff}/time-off", new TimeOffRequest(
            new DateTimeOffset(TestApi.Sast(Tuesday, 12), TimeSpan.Zero),
            new DateTimeOffset(TestApi.Sast(Tuesday, 17), TimeSpan.Zero), "Dentist"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var slots = (await _api.AvailabilityAsync(service, Tuesday)).Slots;
        Assert.Equal(TestApi.Sast(Tuesday, 11), slots[^1].StartUtc); // last slot that ends by 12:00
    }

    [Fact]
    public async Task Only_dates_within_the_booking_window_have_slots()
    {
        var service = await _api.CreateServiceAsync();
        await _api.CreateStaffAsync(service);

        Assert.Empty((await _api.AvailabilityAsync(service, new DateOnly(2030, 1, 5))).Slots); // in the past
        Assert.Empty((await _api.AvailabilityAsync(service, Tuesday.AddDays(90))).Slots);      // beyond 60 days
        Assert.NotEmpty((await _api.AvailabilityAsync(service, Tuesday.AddDays(50))).Slots);
    }

    [Fact]
    public async Task Today_respects_the_minimum_notice_period()
    {
        var service = await _api.CreateServiceAsync(durationMinutes: 30);
        await _api.CreateStaffAsync(service, [new WorkingHoursDto(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(12, 0))]);
        var today = new DateOnly(2030, 1, 7);

        var slots = (await _api.AvailabilityAsync(service, today)).Slots;

        // Now is 08:00 with 60 minutes' notice required, so 09:00 is the first bookable time.
        Assert.Equal(TestApi.Sast(today, 9), slots[0].StartUtc);
    }
}
