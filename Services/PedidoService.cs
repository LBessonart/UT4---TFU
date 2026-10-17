using MaquetaApi.Components;
using MaquetaApi.Models;
using Microsoft.Extensions.Logging;

namespace MaquetaApi.Services;

public sealed class PedidoService(
    IDescuento descuento,
    ICatalogo catalogo,
    IPersistencia persistencia,
    ILogger<PedidoService> logger) : IPedidoService
{
    public async Task<Pedido> ProcesarPedidoAsync(Pedido pedido, bool forzarError = false, CancellationToken cancellationToken = default)
    {
        var productos = await catalogo.ObtenerProductosAsync(cancellationToken);
        foreach (var item in pedido.Items)
        {
            var producto = productos.FirstOrDefault(producto => producto.Id == item.ProductoId);
            if (producto is null)
            {
                throw new InvalidOperationException($"El producto '{item.ProductoId}' no existe.");
            }

            if (item.Cantidad <= 0)
            {
                throw new InvalidOperationException($"La cantidad del producto '{item.ProductoId}' debe ser mayor que cero.");
            }

            item.Nombre = producto.Nombre;
            item.PrecioUnitario = producto.Precio;
        }

        pedido.Total = descuento.Aplicar(pedido.Subtotal);
        var pedidoProcesado = await persistencia.GuardarPedidoConStockAsync(pedido, forzarError, cancellationToken);
        logger.LogInformation("Pedido {Id} procesado exitosamente con total {Total}", pedido.Id, pedido.Total);
        return pedidoProcesado;
    }
}
