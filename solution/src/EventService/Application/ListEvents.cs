using BuildingBlocks.Contracts;
using EventService.Infrastructure;

namespace EventService.Application;

public sealed class ListEvents(EventRepository repo)
{
    public async Task<IReadOnlyList<EventDto>> HandleAsync(int limit, CancellationToken ct)
    {
        var events = await repo.ListPublishedAsync(limit, ct);
        return events.Select(Mapper.ToDto).ToList();
    }
}
