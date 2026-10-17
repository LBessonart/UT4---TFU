using MaquetaApi.Models;

namespace MaquetaApi.Components;

public interface IPersistencia
{
    Task<Pedido> GuardarPedidoConStockAsync(Pedido pedido, bool forzarError = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Pedido>> ObtenerPedidosAsync(CancellationToken cancellationToken = default);
}