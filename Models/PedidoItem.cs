namespace MaquetaApi.Models
{
    public class PedidoItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid PedidoId { get; set; }
        public string ProductoId { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
    }
}
