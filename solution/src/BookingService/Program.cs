var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "booking" }));

// US3 (pendente): implementar reserva atômica (SET NX + Lua GA), Stripe e efetivação em /v1/bookings
app.MapPost("/v1/bookings/reserve", () => Results.Json(
    new { error = "not_implemented" },
    statusCode: StatusCodes.Status501NotImplemented));

app.Run();
