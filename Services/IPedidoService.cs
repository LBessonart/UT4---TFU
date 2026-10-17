using MaquetaApi.Models;

namespace MaquetaApi.Services
{
    public interface IPedidoService
    {
        Task<Pedido> ProcesarPedidoAsync(Pedido pedido, bool forzarError = false, CancellationToken cancellationToken = default);
    }
}
