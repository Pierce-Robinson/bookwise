namespace Bookwise.Api.Domain;

/// <summary>Something a customer can book, such as "Haircut (45 min)".</summary>
public class Service
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public List<StaffMember> Staff { get; set; } = [];
}

public class StaffMember
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Service> Services { get; set; } = [];
    public List<WorkingHours> WorkingHours { get; set; } = [];
    public List<TimeOff> TimeOff { get; set; } = [];
}

/// <summary>
/// A block of working time on a weekday, in the business's local time. A day can have several
/// blocks, for example 09:00–13:00 and 14:00–17:00 around a lunch break.
/// </summary>
public class WorkingHours
{
    public int Id { get; set; }
    public int StaffMemberId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
}

/// <summary>Leave, training or anything else that blocks a staff member's calendar.</summary>
public class TimeOff
{
    public int Id { get; set; }
    public int StaffMemberId { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string? Reason { get; set; }
}

public enum BookingStatus
{
    Confirmed,
    Cancelled,
    Completed,
    NoShow
}

public class Booking
{
    public int Id { get; set; }

    /// <summary>Short, human-friendly code customers quote, such as BK-7F3K2Q.</summary>
    public required string Reference { get; set; }

    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;
    public int StaffMemberId { get; set; }
    public StaffMember StaffMember { get; set; } = null!;

    public required string CustomerName { get; set; }
    public required string CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    /// <summary>Price when booked, so later price changes don't alter existing bookings.</summary>
    public decimal Price { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;

    /// <summary>SHA-256 hash of the customer's cancel token. The token itself is never stored.</summary>
    public required string CancelTokenHash { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime? CancelledUtc { get; set; }
    public DateTime? ReminderSentUtc { get; set; }
}

public class AdminUser
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public string PasswordHash { get; set; } = "";
}
