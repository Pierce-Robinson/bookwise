using System.ComponentModel.DataAnnotations;
using Bookwise.Api.Data;
using Bookwise.Api.Domain;
using Bookwise.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bookwise.Api.Features.Admin;

public record StaffRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [EmailAddress, StringLength(200)] string? Email);

public record WorkingHoursDto(DayOfWeek DayOfWeek, TimeOnly Start, TimeOnly End);

public record AssignServicesRequest([Required] List<int> ServiceIds);

public record TimeOffRequest(
    [Required] DateTimeOffset? Start,
    [Required] DateTimeOffset? End,
    [StringLength(200)] string? Reason);

public record TimeOffResponse(int Id, DateTime StartUtc, DateTime EndUtc, string? Reason);

public record StaffResponse(int Id, string Name, string? Email, bool IsActive, IReadOnlyList<int> ServiceIds,
    IReadOnlyList<WorkingHoursDto> WorkingHours)
{
    public static StaffResponse From(StaffMember s) => new(s.Id, s.Name, s.Email, s.IsActive,
        s.Services.Select(x => x.Id).Order().ToList(),
        s.WorkingHours.OrderBy(w => w.DayOfWeek).ThenBy(w => w.Start)
            .Select(w => new WorkingHoursDto(w.DayOfWeek, w.Start, w.End)).ToList());
}

[ApiController]
[Route("api/admin/staff")]
[Authorize]
[Tags("Admin: staff")]
public class StaffController(BookwiseDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<StaffResponse>> List(CancellationToken ct) =>
        (await WithDetails().OrderBy(s => s.Name).ToListAsync(ct)).Select(StaffResponse.From).ToList();

    [HttpGet("{id:int}")]
    public async Task<StaffResponse> Get(int id, CancellationToken ct) => StaffResponse.From(await Find(id, ct));

    [HttpPost]
    [ProducesResponseType<StaffResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(StaffRequest request, CancellationToken ct)
    {
        var staff = new StaffMember { Name = request.Name.Trim(), Email = request.Email?.Trim() };
        db.Staff.Add(staff);
        await db.SaveChangesAsync(ct);
        return Created($"/api/admin/staff/{staff.Id}", StaffResponse.From(staff));
    }

    /// <summary>Replace the weekly schedule. Several blocks per day are allowed, but they can't overlap.</summary>
    [HttpPut("{id:int}/working-hours")]
    public async Task<StaffResponse> SetWorkingHours(int id, List<WorkingHoursDto> hours, CancellationToken ct)
    {
        foreach (var day in hours.GroupBy(h => h.DayOfWeek))
        {
            var blocks = day.OrderBy(h => h.Start).ToList();
            if (blocks.Any(b => b.End <= b.Start))
            {
                throw new DomainException("INVALID_WORKING_HOURS", $"On {day.Key}, every block must end after it starts.");
            }
            if (blocks.Zip(blocks.Skip(1)).Any(pair => pair.Second.Start < pair.First.End))
            {
                throw new DomainException("INVALID_WORKING_HOURS", $"On {day.Key}, working hour blocks overlap.");
            }
        }

        var staff = await Find(id, ct);
        staff.WorkingHours.Clear();
        staff.WorkingHours.AddRange(hours.Select(h => new WorkingHours
        {
            DayOfWeek = h.DayOfWeek,
            Start = h.Start,
            End = h.End,
        }));
        await db.SaveChangesAsync(ct);
        return StaffResponse.From(staff);
    }

    /// <summary>Set which services this staff member offers.</summary>
    [HttpPut("{id:int}/services")]
    public async Task<StaffResponse> SetServices(int id, AssignServicesRequest request, CancellationToken ct)
    {
        var staff = await Find(id, ct);
        var services = await db.Services.Where(s => request.ServiceIds.Contains(s.Id)).ToListAsync(ct);
        var missing = request.ServiceIds.Except(services.Select(s => s.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException($"Services not found: {string.Join(", ", missing)}.");
        }
        staff.Services.Clear();
        staff.Services.AddRange(services);
        await db.SaveChangesAsync(ct);
        return StaffResponse.From(staff);
    }

    /// <summary>Block out time. Existing bookings in that period are not cancelled automatically.</summary>
    [HttpPost("{id:int}/time-off")]
    [ProducesResponseType<TimeOffResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddTimeOff(int id, TimeOffRequest request, CancellationToken ct)
    {
        var staff = await Find(id, ct);
        var start = request.Start!.Value.UtcDateTime;
        var end = request.End!.Value.UtcDateTime;
        if (end <= start)
        {
            throw new DomainException("INVALID_TIME_OFF", "Time off must end after it starts.");
        }
        var timeOff = new TimeOff { StaffMemberId = staff.Id, StartUtc = start, EndUtc = end, Reason = request.Reason };
        db.TimeOff.Add(timeOff);
        await db.SaveChangesAsync(ct);
        return Created($"/api/admin/staff/{id}/time-off/{timeOff.Id}",
            new TimeOffResponse(timeOff.Id, timeOff.StartUtc, timeOff.EndUtc, timeOff.Reason));
    }

    [HttpDelete("{id:int}/time-off/{timeOffId:int}")]
    public async Task<IActionResult> RemoveTimeOff(int id, int timeOffId, CancellationToken ct)
    {
        var timeOff = await db.TimeOff.FirstOrDefaultAsync(t => t.Id == timeOffId && t.StaffMemberId == id, ct)
            ?? throw new NotFoundException($"Time off {timeOffId} was not found.");
        db.TimeOff.Remove(timeOff);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Stop taking bookings for this staff member. Their history is kept.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        (await Find(id, ct)).IsActive = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private IQueryable<StaffMember> WithDetails() =>
        db.Staff.Include(s => s.Services).Include(s => s.WorkingHours).AsSplitQuery();

    private async Task<StaffMember> Find(int id, CancellationToken ct) =>
        await WithDetails().FirstOrDefaultAsync(s => s.Id == id, ct)
        ?? throw new NotFoundException($"Staff member {id} was not found.");
}
