using System.ComponentModel.DataAnnotations;
using Bookwise.Api.Domain;

namespace Bookwise.Api.Features.Bookings;

public record CreateBookingRequest(
    [Required] int? ServiceId,
    int? StaffId,
    [Required] DateTimeOffset? Start,
    [Required, StringLength(100, MinimumLength = 2)] string CustomerName,
    [Required, EmailAddress, StringLength(200)] string CustomerEmail,
    [Phone, StringLength(30)] string? CustomerPhone);

public record CancelBookingRequest([Required] string Token);

/// <summary>Returned once, when a booking is made. The cancel token is never shown again.</summary>
public record BookingCreatedResponse(string Reference, string CancelToken, BookingResponse Booking);

public record BookingResponse(
    int Id,
    string Reference,
    BookingStatus Status,
    int ServiceId,
    string ServiceName,
    int StaffId,
    string StaffName,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTimeOffset StartLocal,
    decimal Price,
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone)
{
    public static BookingResponse From(Booking b, TimeZoneInfo zone) => new(
        b.Id, b.Reference, b.Status, b.ServiceId, b.Service.Name, b.StaffMemberId, b.StaffMember.Name,
        b.StartUtc, b.EndUtc, Availability.AvailabilityService.ToLocal(b.StartUtc, zone), b.Price,
        b.CustomerName, b.CustomerEmail, b.CustomerPhone);
}
