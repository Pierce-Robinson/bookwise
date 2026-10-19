using System.Security.Cryptography;
using System.Text;
using Bookwise.Api.Data;
using Bookwise.Api.Domain;
using Bookwise.Api.Features.Availability;
using Bookwise.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bookwise.Api.Features.Bookings;

public class BookingService(
    BookwiseDbContext db,
    AvailabilityService availability,
    IOptions<BusinessOptions> options,
    TimeProvider clock)
{
    private const string ReferenceAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // no 0/O or 1/I/L
    private readonly BusinessOptions _business = options.Value;

    /// <summary>
    /// Books a slot. The availability check and the insert happen inside one write transaction
    /// (SQLite BEGIN IMMEDIATE), so two customers racing for the same slot can't both get it.
    /// </summary>
    public async Task<BookingCreatedResponse> CreateAsync(CreateBookingRequest request, CancellationToken ct)
    {
        var startUtc = request.Start!.Value.UtcDateTime;
        var zone = _business.TimeZone;
        var date = AvailabilityCalculator.LocalDate(startUtc, zone);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var service = await availability.LoadBookableServiceAsync(request.ServiceId!.Value, ct);
        var candidates = service.Staff.Where(s => request.StaffId is null || s.Id == request.StaffId).ToList();
        if (request.StaffId is not null && candidates.Count == 0)
        {
            throw new NotFoundException($"Staff member {request.StaffId} does not offer {service.Name}.");
        }

        var slots = await availability.CalculateForStaffAsync(service, candidates, date, ct);
        var available = candidates.Where(s => slots[s.Id].Contains(startUtc)).ToList();
        if (available.Count == 0)
        {
            throw new DomainException("SLOT_UNAVAILABLE",
                "That time is not available. Please choose another slot.");
        }

        var staff = await PickLeastBusyAsync(available, date, ct);
        var token = Base64UrlToken();
        var booking = new Booking
        {
            Reference = await NewReferenceAsync(ct),
            ServiceId = service.Id,
            Service = service,
            StaffMemberId = staff.Id,
            StaffMember = staff,
            CustomerName = request.CustomerName.Trim(),
            CustomerEmail = request.CustomerEmail.Trim().ToLowerInvariant(),
            CustomerPhone = request.CustomerPhone?.Trim(),
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(service.DurationMinutes),
            Price = service.Price,
            CancelTokenHash = Hash(token),
            CreatedUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new BookingCreatedResponse(booking.Reference, token, BookingResponse.From(booking, zone));
    }

    /// <summary>Customer self-service: view a booking with the reference and token from their confirmation.</summary>
    public async Task<BookingResponse> GetForCustomerAsync(string reference, string token, CancellationToken ct)
    {
        var booking = await FindForCustomerAsync(reference, token, ct);
        return BookingResponse.From(booking, _business.TimeZone);
    }

    /// <summary>Customer self-service cancellation, allowed up to the cut-off before the appointment.</summary>
    public async Task<BookingResponse> CancelByCustomerAsync(string reference, string token, CancellationToken ct)
    {
        var booking = await FindForCustomerAsync(reference, token, ct);
        EnsureConfirmed(booking);
        var now = clock.GetUtcNow().UtcDateTime;
        if (booking.StartUtc - now < TimeSpan.FromHours(_business.CancellationCutoffHours))
        {
            throw new DomainException("CANCELLATION_WINDOW_PASSED",
                $"Bookings can only be cancelled online up to {_business.CancellationCutoffHours} hours before. Please call us.");
        }
        booking.Status = BookingStatus.Cancelled;
        booking.CancelledUtc = now;
        await db.SaveChangesAsync(ct);
        return BookingResponse.From(booking, _business.TimeZone);
    }

    public async Task<IReadOnlyList<BookingResponse>> ListForDayAsync(DateOnly date, int? staffId, CancellationToken ct)
    {
        var zone = _business.TimeZone;
        var dayStart = AvailabilityCalculator.ToUtc(date, TimeOnly.MinValue, zone);
        var dayEnd = AvailabilityCalculator.ToUtc(date.AddDays(1), TimeOnly.MinValue, zone);
        var bookings = await db.Bookings
            .Include(b => b.Service)
            .Include(b => b.StaffMember)
            .Where(b => b.StartUtc >= dayStart && b.StartUtc < dayEnd)
            .Where(b => staffId == null || b.StaffMemberId == staffId)
            .OrderBy(b => b.StartUtc)
            .ToListAsync(ct);
        return bookings.Select(b => BookingResponse.From(b, zone)).ToList();
    }

    /// <summary>Staff actions: cancel at any time, or record the outcome once the appointment has started.</summary>
    public async Task<BookingResponse> SetStatusByStaffAsync(int id, BookingStatus status, CancellationToken ct)
    {
        var booking = await db.Bookings.Include(b => b.Service).Include(b => b.StaffMember)
            .FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException($"Booking {id} was not found.");
        EnsureConfirmed(booking);

        var now = clock.GetUtcNow().UtcDateTime;
        if (status is BookingStatus.Completed or BookingStatus.NoShow && booking.StartUtc > now)
        {
            throw new DomainException("APPOINTMENT_NOT_STARTED",
                "An appointment can only be marked completed or no-show once it has started.");
        }
        booking.Status = status;
        if (status == BookingStatus.Cancelled)
        {
            booking.CancelledUtc = now;
        }
        await db.SaveChangesAsync(ct);
        return BookingResponse.From(booking, _business.TimeZone);
    }

    private async Task<Booking> FindForCustomerAsync(string reference, string token, CancellationToken ct)
    {
        var booking = await db.Bookings.Include(b => b.Service).Include(b => b.StaffMember)
            .FirstOrDefaultAsync(b => b.Reference == reference.ToUpperInvariant(), ct);
        // Same error for "no such booking" and "wrong token", so references can't be probed.
        if (booking is null || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(booking.CancelTokenHash), Encoding.ASCII.GetBytes(Hash(token ?? ""))))
        {
            throw new NotFoundException("Booking not found. Check the reference and link from your confirmation.");
        }
        return booking;
    }

    private static void EnsureConfirmed(Booking booking)
    {
        if (booking.Status != BookingStatus.Confirmed)
        {
            throw new DomainException("BOOKING_NOT_ACTIVE", $"Booking {booking.Reference} is already {booking.Status}.");
        }
    }

    /// <summary>When the customer didn't choose, spread work evenly: pick whoever has the fewest bookings that day.</summary>
    private async Task<StaffMember> PickLeastBusyAsync(List<StaffMember> staff, DateOnly date, CancellationToken ct)
    {
        if (staff.Count == 1)
        {
            return staff[0];
        }
        var zone = _business.TimeZone;
        var dayStart = AvailabilityCalculator.ToUtc(date, TimeOnly.MinValue, zone);
        var dayEnd = AvailabilityCalculator.ToUtc(date.AddDays(1), TimeOnly.MinValue, zone);
        var ids = staff.Select(s => s.Id).ToList();
        var counts = await db.Bookings
            .Where(b => ids.Contains(b.StaffMemberId) && b.Status == BookingStatus.Confirmed
                        && b.StartUtc >= dayStart && b.StartUtc < dayEnd)
            .GroupBy(b => b.StaffMemberId)
            .Select(g => new { StaffId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StaffId, x => x.Count, ct);
        return staff.OrderBy(s => counts.GetValueOrDefault(s.Id)).ThenBy(s => s.Id).First();
    }

    private async Task<string> NewReferenceAsync(CancellationToken ct)
    {
        while (true)
        {
            var reference = "BK-" + RandomNumberGenerator.GetString(ReferenceAlphabet, 6);
            if (!await db.Bookings.AnyAsync(b => b.Reference == reference, ct))
            {
                return reference;
            }
        }
    }

    private static string Base64UrlToken() =>
        System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
