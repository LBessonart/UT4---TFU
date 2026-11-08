using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServicioPedidos.Data;
using ServicioPedidos.Integracion;
using ServicioPedidos.Models;
using ServicioPedidos.Strategies;

namespace ServicioPedidos.Services;

/// <summary>Error de negocio del pedido, con el código HTTP que corresponde devolver.</summary>
public sealed class PedidoException(int status, string codigo, string mensaje) : Exception(mensaje)
{
    public int Status { get; } = status;
    public string Codigo { get; } = codigo;
}

public sealed record ResultadoCreacion(Pedido Pedido, bool Creado, int CacheHits, int CacheMisses);

/// <summary>
/// IPedidoService: orquesta la creación de un pedido según el diagrama de secuencia de la TFU5.
///   1. Valida el cliente            -> Servicio de Clientes (REST/JSON)
///   2. Obtiene cada producto        -> Caché (Cache-Aside) o Servicio de Catálogo (SOAP GetProducto)
///   3. Reserva el stock             -> Servicio de Catálogo (SOAP ReservarStock, idempotente)
///   4. Persiste el pedido           -> BD Pedidos (transacción local)
///   Si 4 falla, se compensa con LiberarStock (consistencia eventual, estilo Saga).
/// </summary>
public sealed class PedidoService(
    PedidosDbContext db,
    ClientesClient clientes,
    ProductSoapClient catalogo,
    CatalogoCache cache,
    IDiscountStrategy descuento,
    ILogger<PedidoService> logger)
{
    public async Task<ResultadoCreacion> CrearAsync(CrearPedidoRequest solicitud, CancellationToken ct)
    {
        Validar(solicitud);
        var pedidoId = solicitud.Id is { } id && id != Guid.Empty ? id : Guid.NewGuid();

        // Idempotencia hacia el cliente: reenviar el mismo pedido no lo duplica
        if (await BuscarAsync(pedidoId, ct) is { } existente)
        {
            return new ResultadoCreacion(existente, Creado: false, 0, 0);
        }

        // 1. Cliente
        if (!await clientes.ValidarAsync(solicitud.ClienteId, ct))
        {
            throw new PedidoException(StatusCodes.Status404NotFound, "ClienteNoEncontrado", $"El cliente '{solicitud.ClienteId}' no existe.");
        }

        // 2. Productos: un producto por id distinto, consultados en paralelo (la latencia no se suma ítem a ítem)
        var consultas = await Task.WhenAll(solicitud.Items
            .Select(i => i.ProductoId)
            .Distinct()
            .Select(productoId => ObtenerProductoAsync(productoId, ct)));

        var productos = new Dictionary<string, ProductoInfo>();
        foreach (var (productoId, producto, _) in consultas)
        {
            productos[productoId] = producto
                ?? throw new PedidoException(StatusCodes.Status400BadRequest, "ProductoNoEncontrado", $"El producto '{productoId}' no existe.");
        }

        var hits = consultas.Count(c => c.DesdeCache);
        var misses = consultas.Length - hits;

        var pedido = new Pedido
        {
            Id = pedidoId,
            ClienteId = solicitud.ClienteId,
            Items = solicitud.Items.Select(i => new PedidoItem
            {
                PedidoId = pedidoId,
                ProductoId = i.ProductoId,
                Nombre = productos[i.ProductoId].Nombre,
                Cantidad = i.Cantidad,
                PrecioUnitario = productos[i.ProductoId].Precio
            }).ToList()
        };
        pedido.Total = Math.Round(descuento.ApplyDiscount(pedido.Subtotal), 2);

        // 3. Reserva de stock (el Retry es seguro porque la operación es idempotente por pedidoId)
        try
        {
            var (estado, yaProcesada) = await catalogo.ReservarStockAsync(pedidoId, solicitud.Items, ct);
            if (estado != "Reservada")
            {
                throw new PedidoException(StatusCodes.Status409Conflict, "PedidoCancelado",
                    $"La reserva del pedido '{pedidoId}' ya fue liberada. Envíe el pedido con un id nuevo.");
            }

            if (yaProcesada)
            {
                logger.LogInformation("La reserva del pedido {Id} ya existía (reintento): no se descontó stock de nuevo", pedidoId);
            }
        }
        catch (CatalogoFaultException ex)
        {
            var status = ex.Codigo == "StockInsuficiente" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            throw new PedidoException(status, ex.Codigo, ex.Message);
        }

        // 4. Transacción local. Con el stock ya reservado se completa aunque el cliente corte la conexión.
        try
        {
            db.Pedidos.Add(pedido);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Otro reintento concurrente con el mismo id ganó. La reserva es la misma (idempotente): no se compensa.
            db.ChangeTracker.Clear();
            var ganador = await BuscarAsync(pedidoId, CancellationToken.None);
            return new ResultadoCreacion(ganador!, Creado: false, hits, misses);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo guardar el pedido {Id}; se compensa liberando el stock", pedidoId);
            await CompensarAsync(pedidoId);
            throw new DependenciaNoDisponibleException("pedidos (base de datos)", ex.Message, 5);
        }

        logger.LogInformation("Pedido {Id} creado: total {Total}, caché {Hits} hits / {Misses} misses", pedidoId, pedido.Total, hits, misses);
        return new ResultadoCreacion(pedido, Creado: true, hits, misses);
    }

    public async Task<IReadOnlyCollection<Pedido>> ListarAsync(CancellationToken ct) =>
        await db.Pedidos.AsNoTracking().Include(p => p.Items).OrderByDescending(p => p.FechaCreacion).ToArrayAsync(ct);

    public Task<Pedido?> BuscarAsync(Guid id, CancellationToken ct) =>
        db.Pedidos.AsNoTracking().Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <summary>Cache-Aside: primero la caché; si no está, SOAP GetProducto y se guarda con TTL.</summary>
    private async Task<(string ProductoId, ProductoInfo? Producto, bool DesdeCache)> ObtenerProductoAsync(string productoId, CancellationToken ct)
    {
        if (await cache.ObtenerAsync(productoId, ct) is { } enCache)
        {
            return (productoId, enCache, true);
        }

        var producto = await catalogo.GetProductoAsync(productoId, ct);
        if (producto is not null)
        {
            await cache.GuardarAsync(producto, ct);
        }

        return (productoId, producto, false);
    }

    private async Task CompensarAsync(Guid pedidoId)
    {
        try
        {
            var estado = await catalogo.LiberarStockAsync(pedidoId, CancellationToken.None);
            logger.LogWarning("Compensación del pedido {Id}: reserva {Estado}", pedidoId, estado);
        }
        catch (Exception ex)
        {
            // Limitación conocida: sin outbox, esta reserva queda huérfana hasta una conciliación manual
            logger.LogCritical(ex, "Compensación PENDIENTE: no se pudo liberar el stock del pedido {Id}", pedidoId);
        }
    }

    private static void Validar(CrearPedidoRequest solicitud)
    {
        if (string.IsNullOrWhiteSpace(solicitud.ClienteId))
        {
            throw new PedidoException(StatusCodes.Status400BadRequest, "SolicitudInvalida", "El clienteId es obligatorio.");
        }

        if (solicitud.Items.Count == 0)
        {
            throw new PedidoException(StatusCodes.Status400BadRequest, "SolicitudInvalida", "El pedido debe tener al menos un ítem.");
        }

        if (solicitud.Items.FirstOrDefault(i => string.IsNullOrWhiteSpace(i.ProductoId) || i.Cantidad <= 0) is { } invalido)
        {
            throw new PedidoException(StatusCodes.Status400BadRequest, "SolicitudInvalida",
                $"La cantidad del producto '{invalido.ProductoId}' debe ser mayor que cero.");
        }
    }
}
