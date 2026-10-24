namespace BuildingBlocks.Contracts;

/// <summary>DTOs compartilhados entre os serviços (contato público estável).</summary>

public sealed record TicketDto(
    Guid Id,
    string? Seat,      // null p/ pista/GA
    string? Area,      // área p/ pista/GA
    string Type,
    decimal Price,
    int Quantity,      // estoque p/ GA (1 p/ assento)
    string Status);

public sealed record EventDto(
    Guid Id,
    Guid VenueId,
    string Description,
    string? Artist,
    DateTimeOffset Date,
    string Type,
    string Status,
    string? Venue,
    string? City,
    decimal? PriceFrom,
    IReadOnlyList<TicketDto> SeatMap);

public sealed record BookingDto(
    Guid Id,
    Guid TicketId,
    Guid UserId,
    string Status,
    string? PaymentRef,
    DateTimeOffset CreatedAt);

public sealed record PendingPaymentDto(
    string PaymentRef,
    string Status);
