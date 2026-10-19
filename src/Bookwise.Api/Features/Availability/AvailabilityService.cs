using Bookwise.Api.Data;
using Bookwise.Api.Domain;
using Bookwise.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bookwise.Api.Features.Availability;

public record AvailableSlot(DateTime StartUtc, DateTimeOffset StartLocal, IReadOnlyList<int> StaffIds);

public record AvailabilityResponse(int ServiceId, DateOnly Date, string TimeZone, IReadOnlyList<AvailableSlot> Slots);

/// <summary>Loads calendars from the database and asks <see cref="AvailabilityCalculator"/> for free slots.</summary>
public class AvailabilityService(BookwiseDbContext db, IOptions<BusinessOptions> options, TimeProvider clock)
{
    private readonly BusinessOptions _business = options.Value;

    public async Task<AvailabilityResponse> GetAvailabilityAsync(int serviceId, DateOnly date, int? staffId, CancellationToken ct)
    {
        var service = await LoadBookableServiceAsync(serviceId, ct);
        var staff = service.Staff.Where(s => staffId is null || s.Id == staffId).ToList();
        if (staffId is not null && staff.Count == 0)
        {
            throw new NotFoundException($"Staff member {staffId} does not offer {service.Name}.");
        }

        var perStaff = await CalculateForStaffAsync(service, staff, date, ct);
        var zone = _business.TimeZone;
        var slots = perStaff
            .SelectMany(kv => kv.Value.Select(start => (start, staffId: kv.Key)))
            .GroupBy(x => x.start)
            .OrderBy(g => g.Key)
            .Select(g => new AvailableSlot(
                g.Key,
                ToLocal(g.Key, zone),
                g.Select(x => x.staffId).Order().ToList()))
            .ToList();

        return new AvailabilityResponse(service.Id, date, _business.TimeZoneId, slots);
    }

    /// <summary>Free slot start times per staff member for one local date.</summary>
    public async Task<Dictionary<int, IReadOnlyList<DateTime>>> CalculateForStaffAsync(
        Service service, IReadOnlyCollection<StaffMember> staff, DateOnly date, CancellationToken ct)
    {
        var zone = _business.TimeZone;
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var today = AvailabilityCalculator.LocalDate(nowUtc, zone);
        var result = staff.ToDictionary(s => s.Id, _ => (IReadOnlyList<DateTime>)[]);
        if (staff.Count == 0 || date < today || date > today.AddDays(_business.MaxDaysAhead))
        {
            return result;
        }

        var dayStart = AvailabilityCalculator.ToUtc(date, TimeOnly.MinValue, zone);
        var dayEnd = AvailabilityCalculator.ToUtc(date.AddDays(1), TimeOnly.MinValue, zone);
        var staffIds = staff.Select(s => s.Id).ToList();

        var bookings = await db.Bookings
            .Where(b => staffIds.Contains(b.StaffMemberId) && b.Status == BookingStatus.Confirmed
                        && b.StartUtc < dayEnd && b.EndUtc > dayStart)
            .Select(b => new { b.StaffMemberId, Range = new TimeRange(b.StartUtc, b.EndUtc) })
            .ToListAsync(ct);
        var timeOff = await db.TimeOff
            .Where(t => staffIds.Contains(t.StaffMemberId) && t.StartUtc < dayEnd && t.EndUtc > dayStart)
            .Select(t => new { t.StaffMemberId, Range = new TimeRange(t.StartUtc, t.EndUtc) })
            .ToListAsync(ct);
        var hours = await db.WorkingHours
            .Where(w => staffIds.Contains(w.StaffMemberId) && w.DayOfWeek == date.DayOfWeek)
            .ToListAsync(ct);

        var earliest = nowUtc.AddMinutes(_business.MinimumNoticeMinutes);
        var duration = TimeSpan.FromMinutes(service.DurationMinutes);
        var step = TimeSpan.FromMinutes(_business.SlotIntervalMinutes);

        foreach (var member in staff)
        {
            var busy = bookings.Where(b => b.StaffMemberId == member.Id).Select(b => b.Range)
                .Concat(timeOff.Where(t => t.StaffMemberId == member.Id).Select(t => t.Range))
                .ToList();
            result[member.Id] = AvailabilityCalculator.CalculateSlots(
                date, duration, hours.Where(h => h.StaffMemberId == member.Id), busy, zone, earliest, step);
        }
        return result;
    }

    public async Task<Service> LoadBookableServiceAsync(int serviceId, CancellationToken ct)
    {
        var service = await db.Services
            .Include(s => s.Staff.Where(st => st.IsActive))
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.IsActive, ct);
        return service ?? throw new NotFoundException($"Service {serviceId} was not found.");
    }

    public static DateTimeOffset ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(new DateTimeOffset(utc, TimeSpan.Zero), zone);
}
