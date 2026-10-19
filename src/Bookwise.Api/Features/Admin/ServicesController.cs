using System.ComponentModel.DataAnnotations;
using Bookwise.Api.Data;
using Bookwise.Api.Domain;
using Bookwise.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bookwise.Api.Features.Admin;

public record ServiceRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [StringLength(500)] string? Description,
    [Range(5, 480)] int DurationMinutes,
    [Range(0, 1_000_000)] decimal Price);

public record ServiceResponse(int Id, string Name, string? Description, int DurationMinutes, decimal Price, bool IsActive)
{
    public static ServiceResponse From(Service s) => new(s.Id, s.Name, s.Description, s.DurationMinutes, s.Price, s.IsActive);
}

[ApiController]
[Route("api/admin/services")]
[Authorize]
[Tags("Admin: services")]
public class ServicesController(BookwiseDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<ServiceResponse>> List(CancellationToken ct) =>
        (await db.Services.OrderBy(s => s.Name).ToListAsync(ct)).Select(ServiceResponse.From).ToList();

    [HttpPost]
    [ProducesResponseType<ServiceResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(ServiceRequest request, CancellationToken ct)
    {
        var service = new Service
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            DurationMinutes = request.DurationMinutes,
            Price = request.Price,
        };
        db.Services.Add(service);
        await db.SaveChangesAsync(ct);
        return Created($"/api/admin/services/{service.Id}", ServiceResponse.From(service));
    }

    /// <summary>Update a service. Existing bookings keep the duration and price they were booked with.</summary>
    [HttpPut("{id:int}")]
    public async Task<ServiceResponse> Update(int id, ServiceRequest request, CancellationToken ct)
    {
        var service = await Find(id, ct);
        service.Name = request.Name.Trim();
        service.Description = request.Description?.Trim();
        service.DurationMinutes = request.DurationMinutes;
        service.Price = request.Price;
        await db.SaveChangesAsync(ct);
        return ServiceResponse.From(service);
    }

    /// <summary>Stop offering a service. It stays in the database for past bookings.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        (await Find(id, ct)).IsActive = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Service> Find(int id, CancellationToken ct) =>
        await db.Services.FindAsync([id], ct) ?? throw new NotFoundException($"Service {id} was not found.");
}
