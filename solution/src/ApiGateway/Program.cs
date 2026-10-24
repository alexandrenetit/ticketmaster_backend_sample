using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Anti-bot (RNF-2): rate limiting por usuário — MVP = fila justa + limite de taxa
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("per-user", ctx =>
    {
        var user = ctx.Request.Headers["X-User-Id"].FirstOrDefault()
                   ?? ctx.Connection.RemoteIpAddress?.ToString()
                   ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(user, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromSeconds(10),
            QueueLimit = 0
        });
    });
});

var app = builder.Build();

app.UseMiddleware<QueueSimulatorMiddleware>();
app.UseRateLimiter();
app.MapReverseProxy();

app.Run();
