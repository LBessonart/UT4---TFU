namespace MaquetaApi.Models;

public sealed class Producto
{
	public string Id { get; set; } = string.Empty;
	public string Nombre { get; set; } = string.Empty;
	public decimal Precio { get; set; }
	public int Stock { get; set; }
}