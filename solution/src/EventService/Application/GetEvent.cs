using BuildingBlocks.Contracts;
using EventService.Infrastructure;

namespace EventService.Application;

/// <summary>Visualiza um evento com cache-aside (RNF-5/6).</summary>
public sealed class GetEvent(EventRepository repo, RedisCacheService cache)
{
    public async Task<EventDto?> HandleAsync(Guid id, CancellationToken ct)
    {
        var cached = await cache.GetEventAsync(id, ct);
        if (cached is not null) return cached;

        var ev = await repo.GetByIdAsync(id, ct);
        if (ev is null) return null;

        var dto = Mapper.ToDto(ev);
        await cache.SetEventAsync(dto, ct);
        return dto;
    }
}
