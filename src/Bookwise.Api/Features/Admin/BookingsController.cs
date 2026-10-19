using System.ComponentModel.DataAnnotations;
using Bookwise.Api.Domain;
using Bookwise.Api.Features.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bookwise.Api.Features.Admin;

[ApiController]
[Route("api/admin/bookings")]
[Authorize]
[Tags("Admin: bookings")]
public class BookingsController(BookingService bookings) : ControllerBase
{
    /// <summary>The day sheet: all bookings on a local date, optionally for one staff member.</summary>
    [HttpGet]
    public Task<IReadOnlyList<BookingResponse>> ListForDay([FromQuery, Required] DateOnly date, [FromQuery] int? staffId,
        CancellationToken ct) => bookings.ListForDayAsync(date, staffId, ct);

    [HttpPost("{id:int}/cancel")]
    public Task<BookingResponse> Cancel(int id, CancellationToken ct) =>
        bookings.SetStatusByStaffAsync(id, BookingStatus.Cancelled, ct);

    [HttpPost("{id:int}/complete")]
    public Task<BookingResponse> Complete(int id, CancellationToken ct) =>
        bookings.SetStatusByStaffAsync(id, BookingStatus.Completed, ct);

    [HttpPost("{id:int}/no-show")]
    public Task<BookingResponse> NoShow(int id, CancellationToken ct) =>
        bookings.SetStatusByStaffAsync(id, BookingStatus.NoShow, ct);
}
