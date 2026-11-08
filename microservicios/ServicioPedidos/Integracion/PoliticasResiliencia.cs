using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace ServicioPedidos.Integracion;

/// <summary>
/// Políticas de resiliencia de las llamadas salientes (CircuitBreakerPolicy en el diagrama de componentes).
/// Es un singleton: el estado del circuito tiene que ser compartido por todas las solicitudes.
///
///   Catálogo (SOAP):  Retry  ->  Circuit Breaker  ->  Timeout por intento
///   Clientes (REST):  Retry  ->  Timeout por intento
///   (el Retry solo reintenta fallas rápidas; los timeouts los cuenta el Circuit Breaker)
///
/// El orden importa: el Timeout es el más interno (limita cada intento), el Circuit Breaker
/// registra cada intento fallido, y el Retry no reintenta cuando el circuito está abierto
/// (BrokenCircuitException no está en su ShouldHandle), así que con el circuito abierto se falla rápido.
/// </summary>
public sealed class PoliticasResiliencia
{
    public ResiliencePipeline Catalogo { get; }
    public ResiliencePipeline Clientes { get; }
    public CircuitBreakerStateProvider EstadoCircuitoCatalogo { get; } = new();
    public TimeSpan DuracionCircuitoAbierto { get; }

    public PoliticasResiliencia(IConfiguration configuration, ILogger<PoliticasResiliencia> logger)
    {
        var config = configuration.GetSection("Resiliencia");
        var timeoutCatalogo = TimeSpan.FromSeconds(config.GetValue("TimeoutCatalogoSegundos", 5));
        var timeoutClientes = TimeSpan.FromSeconds(config.GetValue("TimeoutClientesSegundos", 3));
        var reintentos = config.GetValue("Reintentos", 2);
        DuracionCircuitoAbierto = TimeSpan.FromSeconds(config.GetValue("CircuitoAbiertoSegundos", 15));

        // El Circuit Breaker cuenta todas las fallas de infraestructura: red, 5xx y timeouts.
        // Nunca errores de negocio (SOAP Fault "Client"): esos significan que el catálogo SÍ responde.
        var fallaDeInfraestructura = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<TimeoutRejectedException>()
            .Handle<DependenciaTransitoriaException>();

        // El Retry solo reintenta fallas RÁPIDAS (conexión cortada, 5xx). Un timeout no se reintenta:
        // reintentar contra un servicio lento multiplica la espera (3 x 5 s) y lo sobrecarga más.
        var fallaReintentable = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<DependenciaTransitoriaException>();

        Catalogo = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = fallaReintentable,
                MaxRetryAttempts = reintentos,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    logger.LogWarning("Reintento {Intento} hacia Catálogo tras: {Error}",
                        args.AttemptNumber + 1, args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = fallaDeInfraestructura,
                FailureRatio = config.GetValue("CircuitoRatioFallas", 0.5),
                MinimumThroughput = config.GetValue("CircuitoMinimoLlamadas", 3),
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = DuracionCircuitoAbierto,
                StateProvider = EstadoCircuitoCatalogo,
                OnOpened = args =>
                {
                    logger.LogError("Circuito hacia Catálogo ABIERTO por {Duracion} s: {Error}",
                        args.BreakDuration.TotalSeconds, args.Outcome.Exception?.Message);
                    return default;
                },
                OnHalfOpened = _ =>
                {
                    logger.LogWarning("Circuito hacia Catálogo SEMI-ABIERTO: se deja pasar una llamada de prueba");
                    return default;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("Circuito hacia Catálogo CERRADO: el servicio se recuperó");
                    return default;
                }
            })
            .AddTimeout(timeoutCatalogo)
            .Build();

        Clientes = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = fallaReintentable,
                MaxRetryAttempts = reintentos,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    logger.LogWarning("Reintento {Intento} hacia Clientes tras: {Error}",
                        args.AttemptNumber + 1, args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .AddTimeout(timeoutClientes)
            .Build();
    }
}
