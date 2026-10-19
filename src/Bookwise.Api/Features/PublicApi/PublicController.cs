using System.ComponentModel.DataAnnotations;
using Bookwise.Api.Data;
using Bookwise.Api.Features.Availability;
using Bookwise.Api.Features.Bookings;
using Bookwise.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bookwise.Api.Features.PublicApi;

public record BusinessInfoResponse(string Name, string TimeZone, int SlotIntervalMinutes, int MaxDaysAhead,
    int CancellationCutoffHours);

public record PublicServiceResponse(int Id, string Name, string? Description, int DurationMinutes, decimal Price,
    IReadOnlyList<PublicStaffResponse> Staff);

public record PublicStaffResponse(int Id, string Name);

/// <summary>Endpoints used by the customer booking page. No login needed.</summary>
[ApiController]
[Route("api/public")]
[AllowAnonymous]
[Tags("Public booking")]
public class PublicController(
    BookwiseDbContext db,
    AvailabilityService availability,
    BookingService bookings,
    IOptions<BusinessOptions> options) : ControllerBase
{
    [HttpGet("business")]
    public BusinessInfoResponse Business()
    {
        var b = options.Value;
        return new BusinessInfoResponse(b.Name, b.TimeZoneId, b.SlotIntervalMinutes, b.MaxDaysAhead,
            b.CancellationCutoffHours);
    }

    /// <summary>Active services and the staff who offer them.</summary>
    [HttpGet("services")]
    public async Task<IReadOnlyList<PublicServiceResponse>> Services(CancellationToken ct)
    {
        var services = await db.Services
            .Where(s => s.IsActive)
            .Include(s => s.Staff.Where(st => st.IsActive))
            .AsSplitQuery()
            .ToListAsync(ct);
        return services
            .OrderBy(s => s.Name)
            .Select(s => new PublicServiceResponse(s.Id, s.Name, s.Description, s.DurationMinutes, s.Price,
                s.Staff.OrderBy(st => st.Name).Select(st => new PublicStaffResponse(st.Id, st.Name)).ToList()))
            .ToList();
    }

    /// <summary>Free start times for a service on a date (business local time). Optionally for one staff member.</summary>
    [HttpGet("availability")]
    public Task<AvailabilityResponse> Availability([FromQuery, Required] int serviceId,
        [FromQuery, Required] DateOnly date, [FromQuery] int? staffId, CancellationToken ct) =>
        availability.GetAvailabilityAsync(serviceId, date, staffId, ct);

    /// <summary>Book a slot. Returns a reference and a private cancel token for the customer.</summary>
    [HttpPost("bookings")]
    [EnableRateLimiting(RateLimits.PublicWrites)]
    [ProducesResponseType<BookingCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateBooking(CreateBookingRequest request, CancellationToken ct)
    {
        var created = await bookings.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetBooking), new { reference = created.Reference, token = created.CancelToken }, created);
    }

    /// <summary>View a booking using the reference and token from the confirmation.</summary>
    [HttpGet("bookings/{reference}")]
    public Task<BookingResponse> GetBooking(string reference, [FromQuery, Required] string token, CancellationToken ct) =>
        bookings.GetForCustomerAsync(reference, token, ct);

    /// <summary>Cancel a booking online, up to the cancellation cut-off.</summary>
    [HttpPost("bookings/{reference}/cancel")]
    [EnableRateLimiting(RateLimits.PublicWrites)]
    public Task<BookingResponse> CancelBooking(string reference, CancelBookingRequest request, CancellationToken ct) =>
        bookings.CancelByCustomerAsync(reference, request.Token, ct);
}
