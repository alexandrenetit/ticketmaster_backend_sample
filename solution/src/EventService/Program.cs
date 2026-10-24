using EventService.Application;
using EventService.Domain;
using EventService.Infrastructure;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<EventDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));

builder.Services.AddScoped<EventRepository>();
builder.Services.AddSingleton<RedisCacheService>();
builder.Services.AddScoped<GetEvent>();
builder.Services.AddScoped<ListEvents>();

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ---- Catálogo (US1) ---------------------------------------------------------
app.MapGet("/v1/events", async (ListEvents list, CancellationToken ct) =>
    Results.Ok(await list.HandleAsync(limit: 50, ct)));

app.MapGet("/v1/events/{id:guid}", async (Guid id, GetEvent get, CancellationToken ct) =>
{
    var dto = await get.HandleAsync(id, ct);
    return dto is null
        ? Results.NotFound(new { code = "not_found", message = "evento não encontrado" })
        : Results.Ok(dto);
});

// ---- CRUD administrativo (protegido no ApiGateway via JWT) ------------------
app.MapPost("/v1/events", async (EventInput input, EventRepository repo, CancellationToken ct) =>
{
    var ev = new Event
    {
        Id = Guid.NewGuid(),
        VenueId = input.VenueId,
        Description = input.Description,
        Date = input.Date,
        Type = input.Type,
        Artist = input.Artist,
        Status = "draft"
    };
    repo.Add(ev);
    await repo.SaveAsync(ct);
    return Results.Created($"/v1/events/{ev.Id}", ev.Id);
});

app.MapPut("/v1/events/{id:guid}", async (Guid id, EventInput input, EventRepository repo, CancellationToken ct) =>
{
    var ev = await repo.GetByIdAsync(id, ct);
    if (ev is null) return Results.NotFound(new { code = "not_found", message = "evento não encontrado" });
    ev.Description = input.Description;
    ev.Date = input.Date;
    ev.Type = input.Type;
    ev.Artist = input.Artist;
    ev.Status = input.Status ?? ev.Status;
    ev.UpdatedAt = DateTimeOffset.UtcNow;
    repo.Update(ev);
    await repo.SaveAsync(ct);
    return Results.Ok(ev.Id);
});

app.MapDelete("/v1/events/{id:guid}", async (Guid id, EventRepository repo, CancellationToken ct) =>
{
    var ev = await repo.GetByIdAsync(id, ct);
    if (ev is null) return Results.NotFound(new { code = "not_found", message = "evento não encontrado" });
    repo.Remove(ev);
    await repo.SaveAsync(ct);
    return Results.NoContent();
});

app.Run();

public sealed record EventInput(
    Guid VenueId,
    string Description,
    DateTimeOffset Date,
    string Type,
    string? Artist,
    string? Status);
