using System.Net;
using System.Net.Http.Json;
using Bookwise.Api.Features.Admin;
using Bookwise.Api.Features.Auth;
using Bookwise.Api.Features.Bookings;
using Bookwise.Tests.Support;

namespace Bookwise.Tests;

public class AdminTests(BookwiseFactory factory) : IClassFixture<BookwiseFactory>
{
    private readonly TestApi _api = new(factory);

    [Theory]
    [InlineData("/api/admin/services")]
    [InlineData("/api/admin/staff")]
    [InlineData("/api/admin/bookings?date=2030-01-08")]
    public async Task Admin_endpoints_need_a_token(string url)
    {
        var response = await _api.Public.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_is_rejected()
    {
        var response = await _api.Public.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(BookwiseFactory.AdminEmail, "wrong-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Overlapping_working_hours_are_rejected()
    {
        var service = await _api.CreateServiceAsync();
        var staff = await _api.CreateStaffAsync(service);
        var admin = await _api.AdminAsync();

        var response = await admin.PutAsJsonAsync($"/api/admin/staff/{staff}/working-hours", new[]
        {
            new WorkingHoursDto(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(13, 0)),
            new WorkingHoursDto(DayOfWeek.Monday, new TimeOnly(12, 0), new TimeOnly(17, 0)),
        }, TestApi.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("INVALID_WORKING_HOURS", (await TestApi.ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Staff_can_cancel_inside_the_customer_cut_off()
    {
        var service = await _api.CreateServiceAsync();
        await _api.CreateStaffAsync(service);
        var admin = await _api.AdminAsync();
        var booking = await _api.BookOkAsync(service, TestApi.Sast(new DateOnly(2030, 1, 7), 15));

        var response = await admin.PostAsync($"/api/admin/bookings/{booking.Booking.Id}/cancel", null);

        response.EnsureSuccessStatusCode();
        var again = await admin.PostAsync($"/api/admin/bookings/{booking.Booking.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Deactivated_services_disappear_from_the_booking_page()
    {
        var service = await _api.CreateServiceAsync();
        await _api.CreateStaffAsync(service);
        var admin = await _api.AdminAsync();

        (await admin.DeleteAsync($"/api/admin/services/{service}")).EnsureSuccessStatusCode();

        var publicServices = await _api.Public.GetFromJsonAsync<List<Bookwise.Api.Features.PublicApi.PublicServiceResponse>>(
            "/api/public/services", TestApi.Json);
        Assert.DoesNotContain(publicServices!, s => s.Id == service);
        var book = await _api.BookAsync(service, TestApi.Sast(TestApi.Tuesday, 10));
        Assert.Equal(HttpStatusCode.NotFound, book.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await _api.Public.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}

/// <summary>Uses its own app instance because it moves the clock forward.</summary>
public class DaySheetTests(BookwiseFactory factory) : IClassFixture<BookwiseFactory>
{
    private readonly TestApi _api = new(factory);

    [Fact]
    public async Task The_day_sheet_lists_bookings_and_staff_can_record_outcomes()
    {
        var service = await _api.CreateServiceAsync();
        var staff = await _api.CreateStaffAsync(service);
        var admin = await _api.AdminAsync();
        var booking = await _api.BookOkAsync(service, TestApi.Sast(TestApi.Tuesday, 9, 30));

        var sheet = await admin.GetFromJsonAsync<List<BookingResponse>>(
            $"/api/admin/bookings?date=2030-01-08&staffId={staff}", TestApi.Json);
        var entry = Assert.Single(sheet!);
        Assert.Equal(booking.Reference, entry.Reference);

        // The appointment hasn't happened yet, so it can't be marked as a no-show...
        var early = await admin.PostAsync($"/api/admin/bookings/{entry.Id}/no-show", null);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        // ...but once it has started, it can.
        factory.Clock.SetUtcNow(new DateTimeOffset(TestApi.Sast(TestApi.Tuesday, 9, 45), TimeSpan.Zero));
        var noShow = await admin.PostAsync($"/api/admin/bookings/{entry.Id}/no-show", null);
        noShow.EnsureSuccessStatusCode();
        Assert.Equal("NoShow", (await noShow.Content.ReadFromJsonAsync<BookingResponse>(TestApi.Json))!.Status.ToString());
    }
}
