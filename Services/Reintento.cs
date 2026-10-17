using MaquetaApi.Components;
using Npgsql;

namespace MaquetaApi.Services;

public sealed class Reintento(IConfiguracionExterna config)
{
    public int EsperaBaseMs { get; } = 50;
    public int Factor { get; } = 2;

    // Lee reintentos.maxIntentos de la configuración externa (4 si no existe)
    public Task<int> ObtenerMaxIntentosAsync(CancellationToken cancellationToken = default) =>
        config.ObtenerEnteroAsync("reintentos.maxIntentos", 4, cancellationToken);

    // Solo reintenta errores transitorios de PostgreSQL:
    // 40001 = fallo de serialización, 40P01 = deadlock
    public bool EsTransitorio(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e is PostgresException { SqlState: "40001" or "40P01" })
            {
                return true;
            }
        }
        return false;
    }

    // Espera exponencial (50, 100, 200 ms...) más un poco de azar
    public TimeSpan Espera(int intento)
    {
        var baseMs = EsperaBaseMs * Math.Pow(Factor, intento - 1);
        var azarMs = Random.Shared.Next(0, EsperaBaseMs);
        return TimeSpan.FromMilliseconds(baseMs + azarMs);
    }
}