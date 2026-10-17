using MaquetaApi.Components;
using MaquetaApi.Models;
using Microsoft.Extensions.Caching.Memory;

namespace MaquetaApi.Services;

public sealed class CatalogoConCacheService(
    ICatalogo catalogo,
    IMemoryCache cache,
    ILogger<CatalogoConCacheService> logger) : ICatalogo
{
    private static readonly TimeSpan CacheDuracion = TimeSpan.FromMinutes(5);

    public async Task<Producto> RegistrarProductoAsync(Producto producto, CancellationToken cancellationToken = default)
    {
        var resultado = await catalogo.RegistrarProductoAsync(producto, cancellationToken);
        
        // Invalidar caché después de registrar un nuevo producto
        cache.Remove("productos_all");
        cache.Remove($"producto_{resultado.Id}");
        
        logger.LogInformation("Producto {Id} registrado y caché invalidada", resultado.Id);
        return resultado;
    }

    public async Task<IReadOnlyCollection<Producto>> ObtenerProductosAsync(CancellationToken cancellationToken = default)
    {
        // Cache-Aside: primero buscar en caché
        if (cache.TryGetValue<IReadOnlyCollection<Producto>>("productos_all", out var productos))
        {
            logger.LogDebug("Productos obtenidos desde caché");
            return productos!;
        }

        // Cache miss: buscar en base de datos
        logger.LogDebug("Cache miss - obteniendo productos desde base de datos");
        productos = await catalogo.ObtenerProductosAsync(cancellationToken);

        // Guardar en caché
        cache.Set("productos_all", productos, CacheDuracion);
        logger.LogInformation("Productos cacheados por {Duracion} minutos", CacheDuracion.TotalMinutes);

        return productos;
    }

    public async Task<Producto?> ObtenerProductoAsync(string id, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"producto_{id}";

        // Cache-Aside: primero buscar en caché
        if (cache.TryGetValue<Producto>(cacheKey, out var producto))
        {
            logger.LogDebug("Producto {Id} obtenido desde caché", id);
            return producto;
        }

        // Cache miss: buscar en base de datos
        logger.LogDebug("Cache miss - obteniendo producto {Id} desde base de datos", id);
        producto = await catalogo.ObtenerProductoAsync(id, cancellationToken);

        if (producto is not null)
        {
            // Guardar en caché
            cache.Set(cacheKey, producto, CacheDuracion);
            logger.LogInformation("Producto {Id} cacheado por {Duracion} minutos", id, CacheDuracion.TotalMinutes);
        }

        return producto;
    }
}
