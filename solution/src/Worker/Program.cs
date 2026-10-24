var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "worker" }));

// US2 (pendente): consumir Kafka (CDC Debezium) e indexar no Elasticsearch

app.Run();
