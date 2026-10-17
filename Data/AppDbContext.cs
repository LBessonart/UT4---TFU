using MaquetaApi.Models;
using Microsoft.EntityFrameworkCore;

namespace MaquetaApi.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<PedidoItem> PedidoItems => Set<PedidoItem>();
    public DbSet<Parametro> Parametros => Set<Parametro>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Producto>().HasKey(producto => producto.Id);
        modelBuilder.Entity<Cliente>().HasKey(cliente => cliente.Id);
        modelBuilder.Entity<Pedido>().HasKey(pedido => pedido.Id);
        modelBuilder.Entity<PedidoItem>().HasKey(item => item.Id);
        modelBuilder.Entity<Producto>().Property(producto => producto.Precio).HasPrecision(18, 2);
        modelBuilder.Entity<Pedido>().Property(pedido => pedido.Total).HasPrecision(18, 2);
        modelBuilder.Entity<PedidoItem>().Property(item => item.PrecioUnitario).HasPrecision(18, 2);
        modelBuilder.Entity<Pedido>().Ignore(pedido => pedido.Subtotal);
        modelBuilder.Entity<Pedido>().HasMany(pedido => pedido.Items).WithOne().HasForeignKey(item => item.PedidoId);
        modelBuilder.Entity<Parametro>().ToTable("parametros", "config");
        modelBuilder.Entity<Parametro>().HasKey(p => p.Clave);
    }
}

