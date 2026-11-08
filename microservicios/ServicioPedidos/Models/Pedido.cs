namespace ServicioPedidos.Models;

public sealed class Pedido
{
    public Guid Id { get; set; }
    public string ClienteId { get; set; } = string.Empty;
    public List<PedidoItem> Items { get; set; } = [];
    public decimal Subtotal => Items.Sum(i => i.Cantidad * i.PrecioUnitario);
    public decimal Total { get; set; }
    public string Estado { get; set; } = "Confirmado";
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}

public sealed class PedidoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PedidoId { get; set; }
    public string ProductoId { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
}

/// <summary>
/// Cuerpo de POST /api/pedidos. El Id es opcional: si el cliente lo envía, reintentar
/// el POST con el mismo Id no crea un pedido duplicado (idempotencia de punta a punta).
/// </summary>
public sealed class CrearPedidoRequest
{
    public Guid? Id { get; set; }
    public string ClienteId { get; set; } = string.Empty;
    public List<ItemSolicitado> Items { get; set; } = [];
}

public sealed class ItemSolicitado
{
    public string ProductoId { get; set; } = string.Empty;
    public int Cantidad { get; set; }
}

/// <summary>Datos de producto que devuelve GetProducto (SOAP) y que se guardan en caché. Sin stock.</summary>
public sealed record ProductoInfo(string Id, string Nombre, decimal Precio);
