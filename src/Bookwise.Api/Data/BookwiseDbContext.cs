using Bookwise.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bookwise.Api.Data;

public class BookwiseDbContext(DbContextOptions<BookwiseDbContext> options) : DbContext(options)
{
    public DbSet<Service> Services => Set<Service>();
    public DbSet<StaffMember> Staff => Set<StaffMember>();
    public DbSet<WorkingHours> WorkingHours => Set<WorkingHours>();
    public DbSet<TimeOff> TimeOff => Set<TimeOff>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Everything is stored in UTC; this makes sure values read back are marked as UTC too.
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<BookingStatus>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Service>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Description).HasMaxLength(500);
            e.Property(s => s.Price).HasPrecision(10, 2);
            e.HasMany(s => s.Staff).WithMany(s => s.Services).UsingEntity(j => j.ToTable("StaffServices"));
        });

        model.Entity<StaffMember>(e =>
        {
            e.ToTable("Staff");
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Email).HasMaxLength(200);
            e.HasMany(s => s.WorkingHours).WithOne().HasForeignKey(w => w.StaffMemberId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(s => s.TimeOff).WithOne().HasForeignKey(t => t.StaffMemberId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<TimeOff>(e =>
        {
            e.Property(t => t.Reason).HasMaxLength(200);
            e.HasIndex(t => new { t.StaffMemberId, t.StartUtc });
        });

        model.Entity<Booking>(e =>
        {
            e.Property(b => b.Reference).HasMaxLength(12);
            e.HasIndex(b => b.Reference).IsUnique();
            e.Property(b => b.CustomerName).HasMaxLength(100);
            e.Property(b => b.CustomerEmail).HasMaxLength(200);
            e.Property(b => b.CustomerPhone).HasMaxLength(30);
            e.Property(b => b.Price).HasPrecision(10, 2);
            e.Property(b => b.CancelTokenHash).HasMaxLength(64);
            e.HasIndex(b => new { b.Status, b.ReminderSentUtc, b.StartUtc });

            // Safety net in the database itself: a staff member can never have two confirmed
            // bookings starting at the same moment, even if application checks were bypassed.
            // It also serves the availability lookups, which only read confirmed bookings.
            e.HasIndex(b => new { b.StaffMemberId, b.StartUtc })
                .IsUnique()
                .HasFilter("\"Status\" = 'Confirmed'")
                .HasDatabaseName("UX_Bookings_Staff_Start_Confirmed");

            e.HasOne(b => b.Service).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.StaffMember).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<AdminUser>(e =>
        {
            e.Property(a => a.Email).HasMaxLength(200);
            e.HasIndex(a => a.Email).IsUnique();
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
