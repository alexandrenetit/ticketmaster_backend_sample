using BuildingBlocks.Contracts;
using EventService.Domain;

namespace EventService.Application;

internal static class Mapper
{
    public static EventDto ToDto(Event ev) => new(
        ev.Id,
        ev.VenueId,
        ev.Description,
        ev.Artist,
        ev.Date,
        ev.Type,
        ev.Status,
        ev.Venue?.Description,
        ev.Venue?.City,
        ev.Tickets.Count > 0 ? ev.Tickets.Min(t => t.Price) : null,
        ev.Tickets
            .Select(t => new TicketDto(t.Id, t.Seat, t.Area, t.Type, t.Price, t.Quantity, t.Status))
            .ToList());
}
