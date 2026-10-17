using MaquetaApi.Models;

namespace MaquetaApi.Components;

public interface IClientes
{
    Task<Cliente> RegistrarClienteAsync(Cliente cliente, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Cliente>> ObtenerClientesAsync(CancellationToken cancellationToken = default);
    Task<Cliente?> ObtenerClienteAsync(string id, CancellationToken cancellationToken = default);
}