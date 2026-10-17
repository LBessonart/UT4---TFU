using MaquetaApi.Components;
using MaquetaApi.Data;
using MaquetaApi.Models;
using Microsoft.EntityFrameworkCore;

namespace MaquetaApi.Services;

public sealed class CatalogoService(AppDbContext db) : ICatalogo
{
    public async Task<Producto> RegistrarProductoAsync(Producto producto, CancellationToken cancellationToken = default)
    {
        db.Productos.Add(producto);
        await db.SaveChangesAsync(cancellationToken);
        return producto;
    }

    public async Task<IReadOnlyCollection<Producto>> ObtenerProductosAsync(CancellationToken cancellationToken = default) =>
        await db.Productos.AsNoTracking().ToArrayAsync(cancellationToken);

    public Task<Producto?> ObtenerProductoAsync(string id, CancellationToken cancellationToken = default) =>
        db.Productos.AsNoTracking().FirstOrDefaultAsync(producto => producto.Id == id, cancellationToken);
}
