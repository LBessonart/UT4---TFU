using MaquetaApi.Components;
using MaquetaApi.Data;
using Microsoft.EntityFrameworkCore;

namespace MaquetaApi.Services;

public sealed class RefrescoVistaService(
    IServiceScopeFactory scopeFactory,
    ILogger<RefrescoVistaService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var segundos = 30;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguracionExterna>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // El intervalo se lee en cada vuelta: se puede cambiar sin reiniciar
                segundos = await config.ObtenerEnteroAsync("vista.refrescoSegundos", 30, stoppingToken);
                await db.Database.ExecuteSqlRawAsync("REFRESH MATERIALIZED VIEW CONCURRENTLY ventas_por_producto", stoppingToken);
                logger.LogInformation("Vista ventas_por_producto refrescada (proximo refresco en {Segundos} s)", segundos);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "No se pudo refrescar la vista ventas_por_producto");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(segundos, 1)), stoppingToken);
        }
    }
}