using Microsoft.EntityFrameworkCore;
using ServicioCatalogo.Models;

namespace ServicioCatalogo.Data;

public sealed class CatalogoDbContext(DbContextOptions<CatalogoDbContext> options) : DbContext(options)
{
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Reserva> Reservas => Set<Reserva>();
    public DbSet<ReservaItem> ReservaItems => Set<ReservaItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Producto>(e =>
        {
            e.ToTable("Productos");
            e.HasKey(p => p.Id);
            e.Property(p => p.Precio).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Reserva>(e =>
        {
            e.ToTable("Reservas");
            e.HasKey(r => r.PedidoId);
            e.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.PedidoId);
        });

        modelBuilder.Entity<ReservaItem>(e =>
        {
            e.ToTable("ReservaItems");
            e.HasKey(i => i.Id);
        });
    }
}

public static class BaseDeDatos
{
    public static async Task InicializarAsync(IServiceProvider services, ILogger logger)
    {
        // La BD puede tardar en aceptar conexiones (o reiniciarse): se reintenta en el arranque
        for (var intento = 1; ; intento++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<CatalogoDbContext>();
                await db.Database.EnsureCreatedAsync();
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "Productos" ("Id", "Nombre", "Precio", "Stock") VALUES
                    ('prod-001', 'Producto A', 50.00, 100),
                    ('prod-002', 'Producto B', 30.00, 100),
                    ('prod-003', 'Producto C', 20.00, 5)
                    ON CONFLICT ("Id") DO NOTHING;
                    """);
                return;
            }
            catch (Exception ex) when (intento < 15)
            {
                logger.LogWarning("BD de catálogo no disponible (intento {Intento}): {Error}", intento, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }
}
