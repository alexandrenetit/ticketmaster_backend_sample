using System.Text.Json;
using BuildingBlocks.Contracts;
using StackExchange.Redis;

namespace EventService.Infrastructure;

/// <summary>Cache-aside de leitura (padrão cache-aside): miss → banco → grava cache → retorna.</summary>
public sealed class RedisCacheService(IConnectionMultiplexer redis)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly IDatabase _db = redis.GetDatabase();

    public async Task<EventDto?> GetEventAsync(Guid id, CancellationToken ct)
    {
        var json = await _db.StringGetAsync($"event:{id}");
        return json.HasValue ? JsonSerializer.Deserialize<EventDto>(json!) : null;
    }

    public async Task SetEventAsync(EventDto dto, CancellationToken ct) =>
        await _db.StringSetAsync($"event:{dto.Id}", JsonSerializer.Serialize(dto), Ttl);

    public async Task InvalidateAsync(Guid id, CancellationToken ct) =>
        await _db.KeyDeleteAsync($"event:{id}");
}
