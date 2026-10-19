using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bookwise.Api.Features.Admin;
using Bookwise.Api.Features.Auth;
using Bookwise.Api.Features.Availability;
using Bookwise.Api.Features.Bookings;

namespace Bookwise.Tests.Support;

/// <summary>Small helpers so tests read as scenarios rather than HTTP plumbing.</summary>
public class TestApi(BookwiseFactory factory)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Tuesday 8 January 2030: a full working day after the test clock's start.</summary>
    public static readonly DateOnly Tuesday = new(2030, 1, 8);

    public HttpClient Public { get; } = factory.CreateClient();

    private HttpClient? _admin;

    public async Task<HttpClient> AdminAsync()
    {
        if (_admin is not null)
        {
            return _admin;
        }
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(BookwiseFactory.AdminEmail, BookwiseFactory.AdminPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return _admin = client;
    }

    public async Task<int> CreateServiceAsync(int durationMinutes = 60, decimal price = 300m)
    {
        var admin = await AdminAsync();
        var response = await admin.PostAsJsonAsync("/api/admin/services",
            new ServiceRequest($"Service {Guid.NewGuid():N}"[..20], null, durationMinutes, price));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ServiceResponse>(Json))!.Id;
    }

    /// <summary>Creates a staff member offering the given services, working 09:00–17:00 Monday to Saturday.</summary>
    public async Task<int> CreateStaffAsync(int serviceId, IEnumerable<WorkingHoursDto>? hours = null)
    {
        var admin = await AdminAsync();
        var created = await admin.PostAsJsonAsync("/api/admin/staff", new StaffRequest($"Staff {Guid.NewGuid():N}"[..14], null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<StaffResponse>(Json))!.Id;

        hours ??= Enum.GetValues<DayOfWeek>().Where(d => d != DayOfWeek.Sunday)
            .Select(d => new WorkingHoursDto(d, new TimeOnly(9, 0), new TimeOnly(17, 0)));
        (await admin.PutAsJsonAsync($"/api/admin/staff/{id}/working-hours", hours.ToList(), Json)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/admin/staff/{id}/services", new AssignServicesRequest([serviceId]), Json))
            .EnsureSuccessStatusCode();
        return id;
    }

    public async Task<AvailabilityResponse> AvailabilityAsync(int serviceId, DateOnly date, int? staffId = null)
    {
        var url = $"/api/public/availability?serviceId={serviceId}&date={date:yyyy-MM-dd}" +
                  (staffId is null ? "" : $"&staffId={staffId}");
        var response = await Public.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AvailabilityResponse>(Json))!;
    }

    public Task<HttpResponseMessage> BookAsync(int serviceId, DateTime startUtc, int? staffId = null,
        string email = "jane@example.com") =>
        Public.PostAsJsonAsync("/api/public/bookings",
            new CreateBookingRequest(serviceId, staffId, new DateTimeOffset(startUtc, TimeSpan.Zero), "Jane Customer", email, null),
            Json);

    public async Task<BookingCreatedResponse> BookOkAsync(int serviceId, DateTime startUtc, int? staffId = null)
    {
        var response = await BookAsync(serviceId, startUtc, staffId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BookingCreatedResponse>(Json))!;
    }

    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json);

    /// <summary>UTC instant for a local Johannesburg time (UTC+2, no daylight saving).</summary>
    public static DateTime Sast(DateOnly date, int hour, int minute = 0) =>
        date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc).AddHours(-2);
}
