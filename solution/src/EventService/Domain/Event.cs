namespace EventService.Domain;

public sealed class Event
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }
    public string Description { get; set; } = "";
    public DateTimeOffset Date { get; set; }
    public string Type { get; set; } = "";          // show | sport | theater
    public string? Artist { get; set; }
    public string Status { get; set; } = "draft";   // draft | published | sold_out | cancelled
    public string SeatMap { get; set; } = "[]";     // JSONB
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Venue? Venue { get; set; }
    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
}
