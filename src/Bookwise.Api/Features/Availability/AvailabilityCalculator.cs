using Bookwise.Api.Domain;

namespace Bookwise.Api.Features.Availability;

public readonly record struct TimeRange(DateTime StartUtc, DateTime EndUtc)
{
    public bool Overlaps(TimeRange other) => StartUtc < other.EndUtc && other.StartUtc < EndUtc;
}

/// <summary>
/// Pure slot maths with no database or clock, so every rule can be unit tested in isolation.
/// Working hours are in the business's local time; everything else is UTC.
/// </summary>
public static class AvailabilityCalculator
{
    /// <summary>
    /// Returns the UTC start times on <paramref name="date"/> (a local calendar date) at which a
    /// service of <paramref name="duration"/> fits inside working hours, doesn't overlap anything
    /// busy, and starts no earlier than <paramref name="earliestStartUtc"/>.
    /// </summary>
    public static IReadOnlyList<DateTime> CalculateSlots(
        DateOnly date,
        TimeSpan duration,
        IEnumerable<WorkingHours> workingHours,
        IReadOnlyCollection<TimeRange> busy,
        TimeZoneInfo zone,
        DateTime earliestStartUtc,
        TimeSpan step)
    {
        if (duration <= TimeSpan.Zero || step <= TimeSpan.Zero)
        {
            throw new ArgumentException("Duration and step must be positive.");
        }

        var slots = new SortedSet<DateTime>();
        foreach (var window in workingHours.Where(w => w.DayOfWeek == date.DayOfWeek && w.End > w.Start))
        {
            var windowStart = ToUtc(date, window.Start, zone);
            var windowEnd = ToUtc(date, window.End, zone);

            for (var start = windowStart; start + duration <= windowEnd; start += step)
            {
                if (start < earliestStartUtc)
                {
                    continue;
                }

                var candidate = new TimeRange(start, start + duration);
                if (!busy.Any(candidate.Overlaps))
                {
                    slots.Add(start);
                }
            }
        }
        return [.. slots];
    }

    /// <summary>
    /// Converts a local wall-clock time to UTC. Times that don't exist because the clocks went
    /// forward are moved to just after the jump; times that happen twice when the clocks go back
    /// use the first (daylight-saving) occurrence.
    /// </summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            var gap = zone.GetAdjustmentRules()
                .Where(r => r.DateStart <= local && r.DateEnd >= local)
                .Select(r => r.DaylightDelta)
                .DefaultIfEmpty(TimeSpan.FromHours(1))
                .First();
            local = local.Add(gap);
        }
        if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            var daylightOffset = offsets.Max();
            return DateTime.SpecifyKind(local - daylightOffset, DateTimeKind.Utc);
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
}
