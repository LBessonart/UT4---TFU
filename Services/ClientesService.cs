using MaquetaApi.Components;
using MaquetaApi.Data;
using MaquetaApi.Models;
using Microsoft.EntityFrameworkCore;

namespace MaquetaApi.Services;

public sealed class ClientesService(AppDbContext db) : IClientes
{
    public async Task<Cliente> RegistrarClienteAsync(Cliente cliente, CancellationToken cancellationToken = default)
    {
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync(cancellationToken);
        return cliente;
    }

    public async Task<IReadOnlyCollection<Cliente>> ObtenerClientesAsync(CancellationToken cancellationToken = default) =>
        await db.Clientes.AsNoTracking().ToArrayAsync(cancellationToken);

    public Task<Cliente?> ObtenerClienteAsync(string id, CancellationToken cancellationToken = default) =>
        db.Clientes.AsNoTracking().FirstOrDefaultAsync(cliente => cliente.Id == id, cancellationToken);
}
