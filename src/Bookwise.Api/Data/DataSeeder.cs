using Bookwise.Api.Domain;
using Bookwise.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bookwise.Api.Data;

/// <summary>Applies migrations, creates the first admin, and optionally adds a demo salon.</summary>
public static class DataSeeder
{
    public static async Task InitialiseAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<BookwiseDbContext>();
        var seed = provider.GetRequiredService<IOptions<SeedOptions>>().Value;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DataSeeder));

        await db.Database.MigrateAsync();

        if (!await db.AdminUsers.AnyAsync() && !string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            var admin = new AdminUser { Email = seed.AdminEmail.Trim().ToLowerInvariant() };
            admin.PasswordHash = provider.GetRequiredService<IPasswordHasher<AdminUser>>()
                .HashPassword(admin, seed.AdminPassword);
            db.AdminUsers.Add(admin);
            await db.SaveChangesAsync();
            logger.LogInformation("Created admin user {Email}", admin.Email);
        }

        if (seed.DemoData && !await db.Services.AnyAsync())
        {
            AddDemoSalon(db);
            await db.SaveChangesAsync();
            logger.LogInformation("Added demo services and staff");
        }
    }

    private static void AddDemoSalon(BookwiseDbContext db)
    {
        var cut = new Service { Name = "Haircut", Description = "Wash, cut and style", DurationMinutes = 45, Price = 250m };
        var colour = new Service { Name = "Full colour", Description = "All-over colour with toner", DurationMinutes = 90, Price = 650m };
        var beard = new Service { Name = "Beard trim", Description = "Shape and line-up", DurationMinutes = 20, Price = 120m };
        var blowDry = new Service { Name = "Blow-dry", DurationMinutes = 30, Price = 180m };

        db.Staff.AddRange(
            Staff("Thandi Mokoena", [cut, colour, blowDry]),
            Staff("Sipho Ndlovu", [cut, beard]),
            Staff("Lerato Khumalo", [cut, colour, blowDry, beard]));
    }

    private static StaffMember Staff(string name, List<Service> services)
    {
        var staff = new StaffMember { Name = name, Services = services };
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            staff.WorkingHours.Add(new WorkingHours { DayOfWeek = day, Start = new TimeOnly(9, 0), End = new TimeOnly(13, 0) });
            staff.WorkingHours.Add(new WorkingHours { DayOfWeek = day, Start = new TimeOnly(14, 0), End = new TimeOnly(17, 30) });
        }
        staff.WorkingHours.Add(new WorkingHours { DayOfWeek = DayOfWeek.Saturday, Start = new TimeOnly(8, 30), End = new TimeOnly(13, 0) });
        return staff;
    }
}
