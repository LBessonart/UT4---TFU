namespace ServicioCatalogo.Models;

public sealed class Producto
{
    public string Id { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public int Stock { get; set; }
}

/// <summary>
/// Reserva de stock asociada a un pedido. El PedidoId es la clave primaria:
/// es lo que hace idempotente a ReservarStock (una misma reserva no se puede
/// aplicar dos veces, aunque Pedidos reintente la llamada).
/// </summary>
public sealed class Reserva
{
    public Guid PedidoId { get; set; }
    public string Estado { get; set; } = EstadosReserva.Reservada;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaLiberacion { get; set; }
    public List<ReservaItem> Items { get; set; } = [];
}

public sealed class ReservaItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PedidoId { get; set; }
    public string ProductoId { get; set; } = string.Empty;
    public int Cantidad { get; set; }
}

public static class EstadosReserva
{
    public const string Reservada = "Reservada";
    public const string Liberada = "Liberada";
    public const string NoExiste = "NoExiste";
}

public static class CodigosError
{
    public const string ProductoNoEncontrado = "ProductoNoEncontrado";
    public const string StockInsuficiente = "StockInsuficiente";
    public const string SolicitudInvalida = "SolicitudInvalida";
    public const string OperacionDesconocida = "OperacionDesconocida";
    public const string ErrorInterno = "ErrorInterno";
}

/// <summary>Error de negocio del catálogo. Se traduce a un SOAP Fault "Client".</summary>
public sealed class CatalogoException(string codigo, string mensaje) : Exception(mensaje)
{
    public string Codigo { get; } = codigo;
}

public sealed record ItemAReservar(string ProductoId, int Cantidad);

public sealed record ResultadoReserva(Guid PedidoId, string Estado, bool YaProcesada);
