using Microsoft.EntityFrameworkCore;
using ServicioPedidos.Models;

namespace ServicioPedidos.Data;

public sealed class PedidosDbContext(DbContextOptions<PedidosDbContext> options) : DbContext(options)
{
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<PedidoItem> PedidoItems => Set<PedidoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Pedido>(e =>
        {
            e.ToTable("Pedidos");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Total).HasPrecision(18, 2);
            e.Ignore(p => p.Subtotal);
            e.HasMany(p => p.Items).WithOne().HasForeignKey(i => i.PedidoId);
        });

        modelBuilder.Entity<PedidoItem>(e =>
        {
            e.ToTable("PedidoItems");
            e.HasKey(i => i.Id);
            e.Property(i => i.PrecioUnitario).HasPrecision(18, 2);
        });
    }
}

public static class BaseDeDatos
{
    public static async Task InicializarAsync(IServiceProvider services, ILogger logger)
    {
        for (var intento = 1; ; intento++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PedidosDbContext>().Database.EnsureCreatedAsync();
                return;
            }
            catch (Exception ex) when (intento < 15)
            {
                logger.LogWarning("BD de pedidos no disponible (intento {Intento}): {Error}", intento, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }
}
