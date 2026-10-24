# Estrutura da Solution .NET 10 — TicketMaster Platform

> Referência de implementação do backend em **.NET 10 / C# 14** (`net10.0`).
> Esta árvore é o alvo do `tasks.md`; os Dockerfiles dos serviços referenciam estes projetos no `docker-compose.yml`.

---

## 1. Árvore da solution

```text
ticketmaster-platform/
├── TicketMaster.sln
├── Directory.Build.props                 # LangVersion=latest, Nullable, ImplicitUsings, TFM net10.0
├── Directory.Packages.props              # Central Package Management
│
├── src/
│   ├── ApiGateway/                       # YARP + RateLimit + JWT + QueueSimulator (fila dev)
│   │   ├── ApiGateway.csproj
│   │   ├── Program.cs
│   │   ├── gateway.json                  # rotas /v1/events, /v1/search, /v1/bookings
│   │   ├── QueueSimulatorMiddleware.cs   # emula Cloudflare Waiting Room (dev)
│   │   └── Dockerfile
│   │
│   ├── EventService/                     # Catálogo + CRUD + cache-aside (Clean Architecture)
│   │   ├── EventService.csproj
│   │   ├── Program.cs                    # Minimal API
│   │   ├── Domain/                       # Event, Venue, Ticket (agregados/entidades)
│   │   ├── Application/                  # GetEvent, ListEvents, UpsertEvent, CancelEvent
│   │   ├── Infrastructure/               # EventRepository (EF Core), RedisCacheService
│   │   └── Dockerfile
│   │
│   ├── SearchService/                    # Consulta Elasticsearch (fino, sem domínio rico)
│   │   ├── SearchService.csproj
│   │   ├── Program.cs
│   │   └── Dockerfile
│   │
│   ├── BookingService/                   # Reserve (Redis) + Pay (Stripe) + Booking (Postgres)
│   │   ├── BookingService.csproj
│   │   ├── Program.cs
│   │   ├── Application/
│   │   │   ├── ReserveTickets.cs        # SET NX EX 420
│   │   │   ├── ProcessPayment.cs        # Stripe + webhook idempotente
│   │   │   └── ConfirmBooking.cs        # transação ACID no Postgres
│   │   ├── Infrastructure/              # ReservationRepository, BookingRepository, StripeGateway
│   │   └── Dockerfile
│   │
│   ├── Worker/                           # Consome Kafka → indexa Elasticsearch
│   │   ├── Worker.csproj
│   │   ├── Program.cs                    # MassTransit + Confluent.Kafka (BackgroundService)
│   │   ├── Indexers/                     # EventUpserted, EventDeleted, TicketStatusChanged
│   │   └── Dockerfile
│   │
│   └── BuildingBlocks/
│       ├── Contracts/                    # DTOs + eventos (NuGet interno, versionado)
│       │   └── Contracts.csproj
│       └── EventBus/                     # abstrações de mensageria (Kafka)
│           └── EventBus.csproj
│
├── tests/
│   ├── EventService.UnitTests/
│   ├── EventService.IntegrationTests/    # Testcontainers (Postgres/Redis)
│   ├── BookingService.IntegrationTests/  # concorrência da reserva (Testcontainers)
│   ├── ContractTests/                    # Pact / validação do OpenAPI
│   └── LoadTests/                        # NBomber (RNF-001/005)
│
└── infra/                                # docker-compose + configs (ver ../infra/)
```

---

## 2. Responsabilidades por projeto

| Projeto | Responsabilidade | Dono de dados |
|---------|------------------|---------------|
| `ApiGateway` | Reverse proxy (YARP), rate limiting, JWT, fila simulada | — (stateless) |
| `EventService` | Catálogo de eventos/venues/tickets, cache-aside, CRUD | `events`, `venues`, `tickets` (leitura/escrita de catálogo) |
| `SearchService` | Consulta full-text no Elasticsearch | índice `events-index` (leitura) |
| `BookingService` | Reserva temporária (Redis), pagamento (Stripe), efetivação (Postgres) | `bookings`, `tickets.status` (mutação de venda) |
| `Worker` | Consome CDC do Kafka e indexa no Elasticsearch | índice `events-index` (escrita) |
| `BuildingBlocks.Contracts` | DTOs + schemas de eventos compartilhados | — |

---

## 3. Pacotes NuGet (por projeto)

| Projeto | Pacotes-chave |
|---------|---------------|
| `ApiGateway` | `Yarp.ReverseProxy`, `Microsoft.AspNetCore.RateLimiting`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `OpenTelemetry.*` |
| `EventService` | `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `StackExchange.Redis`, `OpenTelemetry.*` |
| `SearchService` | `Elastic.Clients.Elasticsearch` (v8), `OpenTelemetry.*` |
| `BookingService` | `StackExchange.Redis`, `Stripe.net`, `Microsoft.EntityFrameworkCore`, `Npgsql.*`, `OpenTelemetry.*` |
| `Worker` | `MassTransit.Kafka` (ou `Confluent.Kafka`), `Elastic.Clients.Elasticsearch`, `OpenTelemetry.*` |
| Testes | `xunit`, `Testcontainers.*`, `NBomber`, `PactNet`, `Microsoft.AspNetCore.Mvc.Testing` |

> **Central Package Management**: versões centralizadas em `Directory.Packages.props` (Artigo VI — versionamento).

---

## 4. Dockerfile de referência (multi-stage)

Mesmo padrão para todos os serviços (muda apenas o `.csproj`):

```dockerfile
# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/EventService/EventService.csproj", "src/EventService/"]
COPY ["src/BuildingBlocks/Contracts/Contracts.csproj", "src/BuildingBlocks/Contracts/"]
RUN dotnet restore "src/EventService/EventService.csproj"
COPY . .
RUN dotnet publish "src/EventService/EventService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "EventService.dll"]
```

---

## 5. Trecho de código — reserva atômica (anti-oversell)

```csharp
// BookingService/Application/ReserveTickets.cs
public sealed record ReserveTickets(IReadOnlyList<Guid> TicketIds, Guid UserId);

public async Task<ReservationResult> Handle(ReserveTickets cmd, CancellationToken ct)
{
    var acquired = new List<Guid>();
    foreach (var ticketId in cmd.TicketIds)
    {
        // SET key userId NX EX 420 — atômico, nunca GET+SET
        var ok = await _redis.StringSetAsync(
            $"reserve:ticket:{ticketId}", cmd.UserId.ToString(),
            TimeSpan.FromMinutes(7), When.NotExists);

        if (!ok)
        {
            // rollback das reservas já adquiridas nesta requisição
            foreach (var id in acquired)
                await _redis.KeyDeleteAsync($"reserve:ticket:{id}");
            return ReservationResult.Conflict(ticketId);
        }
        acquired.Add(ticketId);
    }
    return ReservationResult.Accepted(acquired, DateTimeOffset.UtcNow.AddMinutes(7));
}
```

---

## 6. Trecho de código — efetivação ACID (consistência forte)

```csharp
// BookingService/Application/ConfirmBooking.cs
public async Task Confirm(Guid ticketId, Guid userId, string paymentRef,
                          string idempotencyKey, CancellationToken ct)
{
    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    var ticket = await _db.Tickets
        .FromSqlInterpolated($"SELECT * FROM tickets WHERE id = {ticketId} FOR UPDATE")
        .SingleOrDefaultAsync(ct);

    if (ticket is null || ticket.Status != "available")
        throw new TicketUnavailableException(ticketId);

    ticket.Status = "booked";

    await _db.Bookings.AddAsync(new Booking
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        UserId = userId,
        PaymentRef = paymentRef,
        IdempotencyKey = idempotencyKey
    }, ct);

    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
}
```

> A defesa em profundidade é `UNIQUE (event_id, seat)` (assento numerado) / `UNIQUE (event_id, area)` (pista/GA) + `UNIQUE (idempotency_key)`; o `FOR UPDATE` garante a exclusão mútua dentro da transação.
