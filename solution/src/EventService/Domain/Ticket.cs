namespace EventService.Domain;

/// <summary>Unidade de venda: assento numerado (Seat != null) OU área de pista/GA (Area != null).</summary>
public sealed class Ticket
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string? Seat { get; set; }   // null p/ pista/GA
    public string? Area { get; set; }   // "pista", "vip", ... p/ GA
    public string Type { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;   // estoque restante (1 p/ assento)
    public int Capacity { get; set; } = 1;   // capacidade inicial da área
    public string Status { get; set; } = "available";
}
