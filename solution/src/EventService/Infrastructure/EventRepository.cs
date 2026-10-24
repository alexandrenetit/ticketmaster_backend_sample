using EventService.Domain;
using Microsoft.EntityFrameworkCore;

namespace EventService.Infrastructure;

public sealed class EventRepository(EventDbContext db)
{
    public async Task<List<Event>> ListPublishedAsync(int limit, CancellationToken ct) =>
        await db.Events
            .Where(e => e.Status == "published")
            .Include(e => e.Venue)
            .Include(e => e.Tickets)
            .OrderBy(e => e.Date)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<Event?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.Events
            .Include(e => e.Venue)
            .Include(e => e.Tickets)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

    public void Add(Event ev) => db.Events.Add(ev);
    public void Update(Event ev) => db.Events.Update(ev);
    public void Remove(Event ev) => db.Events.Remove(ev);
    public async Task SaveAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}
