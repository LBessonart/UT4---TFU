namespace MaquetaApi.Models;

public sealed class VentaPorProducto
{
    public string ProductoId { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public long UnidadesVendidas { get; set; }
    public decimal Ingresos { get; set; }
}