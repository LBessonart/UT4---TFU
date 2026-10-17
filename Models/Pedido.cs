namespace MaquetaApi.Models
{
    public class Pedido
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string ClienteId { get; set; } = string.Empty;
        public List<PedidoItem> Items { get; set; } = new List<PedidoItem>();
        public decimal Subtotal => Items.Sum(i => i.Cantidad * i.PrecioUnitario);
        public decimal Total { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    }
}
