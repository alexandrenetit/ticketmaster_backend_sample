namespace EventService.Domain;

public sealed class Venue
{
    public Guid Id { get; set; }
    public string Description { get; set; } = "";
    public string? Address { get; set; }
    public string? City { get; set; }
    public int Capacity { get; set; }
}
