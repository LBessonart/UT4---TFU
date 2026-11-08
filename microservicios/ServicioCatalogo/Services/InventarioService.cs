using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServicioCatalogo.Data;
using ServicioCatalogo.Models;

namespace ServicioCatalogo.Services;

/// <summary>
/// Lógica de negocio del catálogo (ICatalogoService en el diagrama de componentes).
/// La usan tanto la API REST como el endpoint SOAP.
/// </summary>
public sealed class InventarioService(CatalogoDbContext db, ILogger<InventarioService> logger)
{
    public async Task<IReadOnlyCollection<Producto>> ListarAsync(CancellationToken ct) =>
        await db.Productos.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(ct);

    public Task<Producto?> ObtenerAsync(string id, CancellationToken ct) =>
        db.Productos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Producto> RegistrarAsync(Producto producto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(producto.Id) || producto.Precio < 0 || producto.Stock < 0)
        {
            throw new CatalogoException(CodigosError.SolicitudInvalida, "Id, precio y stock son obligatorios y no negativos.");
        }

        db.Productos.Add(producto);
        await db.SaveChangesAsync(ct);
        return producto;
    }

    /// <summary>
    /// Reserva (descuenta) el stock de todos los ítems de un pedido en una transacción local.
    /// Idempotente por pedidoId: si la reserva ya existe, no vuelve a descontar.
    /// </summary>
    public async Task<ResultadoReserva> ReservarStockAsync(Guid pedidoId, IReadOnlyCollection<ItemAReservar> items, CancellationToken ct)
    {
        if (pedidoId == Guid.Empty)
        {
            throw new CatalogoException(CodigosError.SolicitudInvalida, "El pedidoId es obligatorio.");
        }

        if (items.Count == 0 || items.Any(i => string.IsNullOrWhiteSpace(i.ProductoId) || i.Cantidad <= 0))
        {
            throw new CatalogoException(CodigosError.SolicitudInvalida, "La reserva necesita al menos un ítem con cantidad mayor que cero.");
        }

        // Idempotencia: un reintento de la misma reserva devuelve el resultado original
        var existente = await db.Reservas.AsNoTracking().FirstOrDefaultAsync(r => r.PedidoId == pedidoId, ct);
        if (existente is not null)
        {
            logger.LogInformation("Reserva {PedidoId} ya procesada ({Estado}); no se descuenta de nuevo", pedidoId, existente.Estado);
            return new ResultadoReserva(pedidoId, existente.Estado, YaProcesada: true);
        }

        // Se agrupan por producto y se ordenan para tomar los locks siempre en el mismo orden (evita deadlocks)
        var agrupados = items
            .GroupBy(i => i.ProductoId)
            .Select(g => new ItemAReservar(g.Key, g.Sum(i => i.Cantidad)))
            .OrderBy(i => i.ProductoId, StringComparer.Ordinal)
            .ToList();

        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        foreach (var item in agrupados)
        {
            // UPDATE condicional: descuenta solo si alcanza. Es atómico (lock de fila) aun con concurrencia.
            var filas = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Productos" SET "Stock" = "Stock" - {item.Cantidad} WHERE "Id" = {item.ProductoId} AND "Stock" >= {item.Cantidad}""",
                ct);

            if (filas == 0)
            {
                var existe = await db.Productos.AnyAsync(p => p.Id == item.ProductoId, ct);
                // Al salir sin Commit, la transacción se revierte y no queda stock descontado a medias
                throw existe
                    ? new CatalogoException(CodigosError.StockInsuficiente, $"Stock insuficiente para el producto '{item.ProductoId}'.")
                    : new CatalogoException(CodigosError.ProductoNoEncontrado, $"El producto '{item.ProductoId}' no existe.");
            }
        }

        db.Reservas.Add(new Reserva
        {
            PedidoId = pedidoId,
            Items = agrupados.Select(i => new ReservaItem { PedidoId = pedidoId, ProductoId = i.ProductoId, Cantidad = i.Cantidad }).ToList()
        });

        try
        {
            await db.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Dos reintentos concurrentes del mismo pedido: ganó el otro. Esta transacción se revierte entera.
            await transaccion.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var ganadora = await db.Reservas.AsNoTracking().FirstAsync(r => r.PedidoId == pedidoId, ct);
            return new ResultadoReserva(pedidoId, ganadora.Estado, YaProcesada: true);
        }

        logger.LogInformation("Stock reservado para el pedido {PedidoId} ({Items} productos)", pedidoId, agrupados.Count);
        return new ResultadoReserva(pedidoId, EstadosReserva.Reservada, YaProcesada: false);
    }

    /// <summary>
    /// Compensación: devuelve el stock de una reserva. Idempotente (solo libera si está "Reservada").
    /// </summary>
    public async Task<string> LiberarStockAsync(Guid pedidoId, CancellationToken ct)
    {
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        var filas = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Reservas" SET "Estado" = {EstadosReserva.Liberada}, "FechaLiberacion" = now() WHERE "PedidoId" = {pedidoId} AND "Estado" = {EstadosReserva.Reservada}""",
            ct);

        if (filas == 0)
        {
            var estado = await db.Reservas.AsNoTracking()
                .Where(r => r.PedidoId == pedidoId)
                .Select(r => r.Estado)
                .FirstOrDefaultAsync(ct);
            return estado ?? EstadosReserva.NoExiste;
        }

        var items = await db.ReservaItems.AsNoTracking().Where(i => i.PedidoId == pedidoId).ToListAsync(ct);
        foreach (var item in items.OrderBy(i => i.ProductoId, StringComparer.Ordinal))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Productos" SET "Stock" = "Stock" + {item.Cantidad} WHERE "Id" = {item.ProductoId}""",
                ct);
        }

        await transaccion.CommitAsync(ct);
        logger.LogWarning("Reserva {PedidoId} liberada (compensación)", pedidoId);
        return EstadosReserva.Liberada;
    }
}
