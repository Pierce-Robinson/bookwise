using Bookwise.Api.Domain;
using Bookwise.Api.Features.Availability;

namespace Bookwise.Tests;

public class AvailabilityCalculatorTests
{
    private static readonly TimeZoneInfo Johannesburg = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private static readonly DateOnly Tuesday = new(2030, 1, 8);
    private static readonly TimeSpan FifteenMinutes = TimeSpan.FromMinutes(15);

    private static WorkingHours Hours(DayOfWeek day, int startHour, int endHour, int startMinute = 0, int endMinute = 0) =>
        new() { DayOfWeek = day, Start = new TimeOnly(startHour, startMinute), End = new TimeOnly(endHour, endMinute) };

    private static DateTime Utc(DateOnly date, int hour, int minute = 0) =>
        date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc);

    private static IReadOnlyList<DateTime> Slots(
        TimeSpan duration, IEnumerable<WorkingHours> hours, IReadOnlyCollection<TimeRange>? busy = null,
        DateTime? earliest = null, TimeZoneInfo? zone = null, DateOnly? date = null, TimeSpan? step = null) =>
        AvailabilityCalculator.CalculateSlots(date ?? Tuesday, duration, hours, busy ?? [], zone ?? Johannesburg,
            earliest ?? DateTime.MinValue, step ?? FifteenMinutes);

    [Fact]
    public void Slots_fill_working_hours_and_convert_local_time_to_utc()
    {
        var slots = Slots(TimeSpan.FromHours(1), [Hours(DayOfWeek.Tuesday, 9, 17)]);

        Assert.Equal(Utc(Tuesday, 7), slots[0]);    // 09:00 SAST
        Assert.Equal(Utc(Tuesday, 14), slots[^1]);  // 16:00 SAST, the last start that ends by 17:00
        Assert.Equal(29, slots.Count);              // 09:00 to 16:00 every 15 minutes
    }

    [Fact]
    public void A_service_must_finish_before_a_break()
    {
        var slots = Slots(TimeSpan.FromMinutes(45),
            [Hours(DayOfWeek.Tuesday, 9, 13), Hours(DayOfWeek.Tuesday, 14, 17)]);

        Assert.Contains(Utc(Tuesday, 10, 15), slots);       // 12:15 local, ends 13:00
        Assert.DoesNotContain(Utc(Tuesday, 10, 30), slots); // 12:30 local would run into lunch
        Assert.DoesNotContain(Utc(Tuesday, 11, 30), slots); // 13:30 local is during lunch
        Assert.Contains(Utc(Tuesday, 12), slots);           // 14:00 local, after lunch
    }

    [Fact]
    public void Existing_bookings_block_every_overlapping_slot_but_not_adjacent_ones()
    {
        var booked = new TimeRange(Utc(Tuesday, 8), Utc(Tuesday, 9)); // 10:00–11:00 local
        var slots = Slots(TimeSpan.FromHours(1), [Hours(DayOfWeek.Tuesday, 9, 17)], [booked]);

        Assert.Contains(Utc(Tuesday, 7), slots);           // 09:00–10:00 ends exactly when the booking starts
        Assert.DoesNotContain(Utc(Tuesday, 7, 15), slots); // 09:15–10:15 overlaps
        Assert.DoesNotContain(Utc(Tuesday, 8, 45), slots); // 10:45–11:45 overlaps
        Assert.Contains(Utc(Tuesday, 9), slots);           // 11:00 starts exactly when it ends
    }

    [Fact]
    public void Nothing_is_offered_before_the_earliest_allowed_start()
    {
        var slots = Slots(TimeSpan.FromMinutes(30), [Hours(DayOfWeek.Tuesday, 9, 17)], earliest: Utc(Tuesday, 8, 20));

        Assert.Equal(Utc(Tuesday, 8, 30), slots[0]);
    }

    [Fact]
    public void Days_without_working_hours_have_no_slots()
    {
        Assert.Empty(Slots(TimeSpan.FromHours(1), [Hours(DayOfWeek.Monday, 9, 17)]));
    }

    [Fact]
    public void Clocks_going_forward_shorten_the_day_correctly()
    {
        // In London on Sunday 31 March 2030 the clocks jump from 01:00 to 02:00, so a
        // 00:00–04:00 local shift is only three real hours long.
        var dstDay = new DateOnly(2030, 3, 31);
        var slots = Slots(TimeSpan.FromHours(1), [Hours(DayOfWeek.Sunday, 0, 4)], zone: London, date: dstDay,
            step: TimeSpan.FromHours(1));

        Assert.Equal([Utc(dstDay, 0), Utc(dstDay, 1), Utc(dstDay, 2)], slots);
    }

    [Fact]
    public void A_local_time_skipped_by_daylight_saving_moves_to_just_after_the_jump()
    {
        var utc = AvailabilityCalculator.ToUtc(new DateOnly(2030, 3, 31), new TimeOnly(1, 30), London);

        Assert.Equal(new DateTime(2030, 3, 31, 1, 30, 0, DateTimeKind.Utc), utc); // 02:30 BST
    }

    [Fact]
    public void A_local_time_that_happens_twice_uses_the_first_occurrence()
    {
        // Clocks go back from 02:00 to 01:00 on Sunday 27 October 2030, so 01:30 happens twice.
        var utc = AvailabilityCalculator.ToUtc(new DateOnly(2030, 10, 27), new TimeOnly(1, 30), London);

        Assert.Equal(new DateTime(2030, 10, 27, 0, 30, 0, DateTimeKind.Utc), utc); // 01:30 BST
    }
}
