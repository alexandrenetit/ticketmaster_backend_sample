using EventService.Domain;
using Microsoft.EntityFrameworkCore;

namespace EventService.Infrastructure;

public sealed class EventDbContext(DbContextOptions<EventDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Event>(e =>
        {
            e.ToTable("events");
            e.Property(x => x.SeatMap).HasColumnType("jsonb");
        });

        mb.Entity<Venue>(e => e.ToTable("venues"));

        mb.Entity<Ticket>(e =>
        {
            e.ToTable("tickets");
            // Índices únicos parciais (assento numerado e área GA) — anti-oversell no banco
            e.HasIndex(x => new { x.EventId, x.Seat }).IsUnique().HasFilter("seat IS NOT NULL");
            e.HasIndex(x => new { x.EventId, x.Area }).IsUnique().HasFilter("area IS NOT NULL");
        });
    }
}
