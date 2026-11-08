using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using ServicioPedidos.Models;

namespace ServicioPedidos.Integracion;

/// <summary>
/// Cache-Aside de productos (CatalogoCache, TTL 60 s) sobre el contenedor de caché (Redis).
/// Solo guarda id, nombre y precio: el stock nunca se cachea, lo confirma Catálogo al reservar.
/// Si la caché está caída o lenta se trata como un "miss": la caché acelera, pero no es una dependencia crítica.
/// </summary>
public sealed class CatalogoCache(IDistributedCache cache, IConfiguration config, ILogger<CatalogoCache> logger)
{
    private static readonly TimeSpan LimiteOperacion = TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(config.GetValue("Cache:TtlSegundos", 60));

    private static string Clave(string productoId) => $"producto:{productoId}";

    public async Task<ProductoInfo?> ObtenerAsync(string productoId, CancellationToken ct)
    {
        try
        {
            var json = await cache.GetStringAsync(Clave(productoId), ct).WaitAsync(LimiteOperacion, ct);
            return json is null ? null : JsonSerializer.Deserialize<ProductoInfo>(json);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Caché no disponible al leer {Producto}: {Error}. Se continúa sin caché.", productoId, ex.Message);
            return null;
        }
    }

    public async Task GuardarAsync(ProductoInfo producto, CancellationToken ct)
    {
        try
        {
            var opciones = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl };
            await cache.SetStringAsync(Clave(producto.Id), JsonSerializer.Serialize(producto), opciones, ct)
                .WaitAsync(LimiteOperacion, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Caché no disponible al guardar {Producto}: {Error}", producto.Id, ex.Message);
        }
    }
}
