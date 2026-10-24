namespace ApiGateway;

/// <summary>
/// Simula (apenas em dev) a fila virtual de borda (Cloudflare Waiting Room).
/// Em produção a fila real fica na borda; aqui o middleware apenas registra a
/// passagem (no-op) para manter o fluxo idêntico ao de produção.
/// </summary>
public sealed class QueueSimulatorMiddleware(RequestDelegate next, ILogger<QueueSimulatorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        ctx.Response.Headers["X-Queue-Simulated"] = "pass";
        logger.LogDebug("QueueSimulator: {Path} liberado", ctx.Request.Path);
        await next(ctx);
    }
}
