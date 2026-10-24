var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "search" }));

// US2 (pendente): implementar consulta Elasticsearch (multi_match + filtros type/city) em /v1/search
app.MapGet("/v1/search", () => Results.Json(
    new { items = Array.Empty<object>() },
    statusCode: StatusCodes.Status501NotImplemented));

app.Run();
