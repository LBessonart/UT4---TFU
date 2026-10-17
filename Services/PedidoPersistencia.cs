using System.Data;
using MaquetaApi.Components;
using MaquetaApi.Data;
using MaquetaApi.Models;
using Microsoft.EntityFrameworkCore;

namespace MaquetaApi.Services;

public sealed class PedidoPersistencia(AppDbContext db) : IPersistencia
{
    public async Task<Pedido> GuardarPedidoConStockAsync(Pedido pedido, bool forzarError = false, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        foreach (var item in pedido.Items)
        {
            var producto = await db.Productos.SingleAsync(producto => producto.Id == item.ProductoId, cancellationToken);
            if (producto.Stock < item.Cantidad)
            {
                throw new InvalidOperationException($"Stock insuficiente para el producto '{producto.Id}'.");
            }

            producto.Stock -= item.Cantidad;
        }

        if (forzarError)
        {
            throw new InvalidOperationException("Error simulado para demostrar rollback ACID.");
        }

        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return pedido;
    }

    public async Task<IReadOnlyCollection<Pedido>> ObtenerPedidosAsync(CancellationToken cancellationToken = default) =>
        await db.Pedidos.AsNoTracking().Include(pedido => pedido.Items).ToArrayAsync(cancellationToken);
}
