namespace ServicioCatalogo.Services;

/// <summary>
/// Inyección de fallas para la demo: agrega latencia o hace fallar el endpoint SOAP
/// sin tener que apagar el contenedor. Sirve para observar el Timeout y el Circuit Breaker de Pedidos.
/// </summary>
public sealed class SimulacionFallas
{
    private int _latenciaMs;
    private int _fallar;

    public int LatenciaMs => Volatile.Read(ref _latenciaMs);
    public bool Fallar => Volatile.Read(ref _fallar) == 1;

    public void Configurar(int latenciaMs, bool fallar)
    {
        Volatile.Write(ref _latenciaMs, Math.Clamp(latenciaMs, 0, 60_000));
        Volatile.Write(ref _fallar, fallar ? 1 : 0);
    }

    public object Estado() => new { latenciaMs = LatenciaMs, fallar = Fallar };
}

public sealed record ConfiguracionSimulacion(int LatenciaMs = 0, bool Fallar = false);

public sealed class SimulacionFallasMiddleware(RequestDelegate next, SimulacionFallas simulacion)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path.StartsWithSegments("/ws/productos"))
        {
            if (simulacion.LatenciaMs > 0)
            {
                await Task.Delay(simulacion.LatenciaMs, context.RequestAborted);
            }

            if (simulacion.Fallar)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("Catálogo no disponible (falla simulada)");
                return;
            }
        }

        await next(context);
    }
}
