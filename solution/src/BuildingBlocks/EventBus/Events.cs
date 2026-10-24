namespace BuildingBlocks.EventBus;

/// <summary>Contrato de mensageria (implementado com MassTransit + Confluent.Kafka no Worker).</summary>
public interface IEventBus
{
    Task PublishAsync<T>(T message, string topic, CancellationToken ct = default) where T : class;
}

/// <summary>Eventos de domínio publicados na sincronização CDC → Elasticsearch.</summary>
public sealed record EventUpserted(
    Guid EventId,
    long Version,
    string Description,
    string? Artist,
    string Type,
    DateTimeOffset Date,
    string Status,
    string? Venue,
    string? City,
    decimal PriceMin);

public sealed record EventDeleted(Guid EventId, long Version);

public sealed record TicketStatusChanged(
    Guid TicketId,
    Guid EventId,
    string? Seat,
    string Status,
    long Version);
