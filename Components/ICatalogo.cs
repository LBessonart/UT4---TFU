using MaquetaApi.Models;

namespace MaquetaApi.Components;

public interface ICatalogo
{
    Task<Producto> RegistrarProductoAsync(Producto producto, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Producto>> ObtenerProductosAsync(CancellationToken cancellationToken = default);
    Task<Producto?> ObtenerProductoAsync(string id, CancellationToken cancellationToken = default);
}