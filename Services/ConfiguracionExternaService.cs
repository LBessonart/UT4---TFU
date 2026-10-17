using MaquetaApi.Components;
using MaquetaApi.Data;
using Microsoft.EntityFrameworkCore;

namespace MaquetaApi.Services;

public sealed class ConfiguracionExternaService(AppDbContext db) : IConfiguracionExterna
{
    public async Task<string?> ObtenerAsync(string clave, CancellationToken cancellationToken = default) =>
        await db.Parametros.AsNoTracking()
            .Where(p => p.Clave == clave)
            .Select(p => p.Valor)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> ObtenerEnteroAsync(string clave, int valorPorDefecto, CancellationToken cancellationToken = default)
    {
        var valor = await ObtenerAsync(clave, cancellationToken);
        return int.TryParse(valor, out var numero) ? numero : valorPorDefecto;
    }
}