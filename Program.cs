using MaquetaApi.Components;
using MaquetaApi.Data;
using MaquetaApi.Middleware;
using MaquetaApi.Services;
using MaquetaApi.Strategies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("No se configuró ConnectionStrings:DefaultConnection.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// Configurar estrategia de descuento según appsettings (RNF 3 - Binding en tiempo de configuración)
var discountStrategy = builder.Configuration["DiscountStrategy"]?.ToLower() ?? "percentage";

switch (discountStrategy)
{
    case "percentage":
        builder.Services.AddSingleton<IDiscountStrategy, PercentageDiscountStrategy>();
        break;
    case "fixed":
        builder.Services.AddSingleton<IDiscountStrategy, FixedDiscountStrategy>();
        break;
    default:
        throw new ArgumentException($"Estrategia de descuento no reconocida: {discountStrategy}");
}

builder.Services.AddScoped<IPedidoService, PedidoService>();
builder.Services.AddSingleton<IDescuento>(sp => sp.GetRequiredService<IDiscountStrategy>());
builder.Services.AddScoped<ICatalogo, CatalogoConCacheService>();
builder.Services.AddScoped<IClientes, ClientesService>();
builder.Services.AddScoped<PedidoPersistencia>();
builder.Services.AddScoped<Reintento>();
builder.Services.AddScoped<IPersistencia, PersistenciaConReintentos>();
builder.Services.AddScoped<BalanceadorService>();
builder.Services.AddScoped<IConfiguracionExterna, ConfiguracionExternaService>();
builder.Services.AddHostedService<RefrescoVistaService>();
builder.Services.AddSingleton<ValetKeyService>();
builder.Services.AddControllers();

var app = builder.Build();

await InicializarBaseDeDatosAsync(app.Services);

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Api-Instance"] = builder.Configuration["API_INSTANCE"] ?? "local";
    await next();
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Gatekeeper pattern: middleware de autenticación
app.UseMiddleware<GatekeeperMiddleware>();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/debug/config/{clave}", async (string clave, IConfiguracionExterna config) =>
    Results.Ok(new { clave, valor = await config.ObtenerAsync(clave) }));

app.Run();

static async Task InicializarBaseDeDatosAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await db.Database.OpenConnectionAsync();
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(424242)");
        try
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Productos" ("Id", "Nombre", "Precio", "Stock") VALUES
            ('prod-001', 'Producto A', 50.00, 100),
            ('prod-002', 'Producto B', 30.00, 100)
            ON CONFLICT ("Id") DO NOTHING;
            INSERT INTO "Clientes" ("Id", "Nombre", "Email") VALUES
            ('cliente-test-001', 'Cliente de prueba', 'cliente@example.com')
            ON CONFLICT ("Id") DO NOTHING;
            INSERT INTO config.parametros ("Clave", "Valor") VALUES
            ('reintentos.maxIntentos', '4'),
            ('vista.refrescoSegundos', '30')
            ON CONFLICT ("Clave") DO NOTHING;
            """);

            await db.Database.ExecuteSqlRawAsync(
                "CREATE MATERIALIZED VIEW IF NOT EXISTS ventas_por_producto AS " + ConsultasReporte.VentasPorProducto);
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_ventas_por_producto ON ventas_por_producto (\"ProductoId\")");

        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(424242)");
        }
    }
    finally
    {
        await db.Database.CloseConnectionAsync();
    }
}
