using MaquetaApi.Components;
using MaquetaApi.Models;

namespace MaquetaApi.Services;

public sealed class PersistenciaConReintentos(
    IServiceScopeFactory scopeFactory,
    PedidoPersistencia lectura,
    Reintento reintento,
    ILogger<PersistenciaConReintentos> logger) : IPersistencia
{
    public async Task<Pedido> GuardarPedidoConStockAsync(Pedido pedido, bool forzarError = false, CancellationToken cancellationToken = default)
    {
        var maxIntentos = await reintento.ObtenerMaxIntentosAsync(cancellationToken);

        for (var intento = 1; ; intento++)
        {
            try
            {
                // Un scope nuevo (y por lo tanto un AppDbContext nuevo) por cada intento
                await using var scope = scopeFactory.CreateAsyncScope();
                var persistencia = scope.ServiceProvider.GetRequiredService<PedidoPersistencia>();
                return await persistencia.GuardarPedidoConStockAsync(pedido, forzarError, cancellationToken);
            }
            catch (Exception ex) when (intento < maxIntentos && reintento.EsTransitorio(ex))
            {
                var espera = reintento.Espera(intento);
                logger.LogWarning("Reintento {Intento}/{Max} del pedido {Id} tras error transitorio; espera {Espera} ms",
                    intento, maxIntentos, pedido.Id, (int)espera.TotalMilliseconds);
                await Task.Delay(espera, cancellationToken);
            }
        }
    }

    // Las lecturas no necesitan reintento: se delegan tal cual
    public Task<IReadOnlyCollection<Pedido>> ObtenerPedidosAsync(CancellationToken cancellationToken = default) =>
        lectura.ObtenerPedidosAsync(cancellationToken);
}