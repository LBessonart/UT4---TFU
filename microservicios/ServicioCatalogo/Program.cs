using Microsoft.EntityFrameworkCore;
using ServicioCatalogo.Data;
using ServicioCatalogo.Models;
using ServicioCatalogo.Services;
using ServicioCatalogo.Soap;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Catalogo")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:Catalogo.");

// Base de datos propia del servicio (Database per Service)
builder.Services.AddDbContext<CatalogoDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddScoped<InventarioService>();
builder.Services.AddSingleton<SimulacionFallas>();

var app = builder.Build();

await BaseDeDatos.InicializarAsync(app.Services, app.Logger);

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Servicio"] = "catalogo";
    await next();
});
app.UseMiddleware<SimulacionFallasMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok", servicio = "catalogo" }));

// ---------- API REST (lectura y alta de productos, expuesta por el Gatekeeper) ----------
app.MapGet("/api/productos", async (InventarioService inventario, CancellationToken ct) =>
    Results.Ok(await inventario.ListarAsync(ct)));

app.MapGet("/api/productos/{id}", async (string id, InventarioService inventario, CancellationToken ct) =>
    await inventario.ObtenerAsync(id, ct) is { } producto
        ? Results.Ok(producto)
        : Results.NotFound(new { error = $"Producto '{id}' no encontrado." }));

app.MapPost("/api/productos", async (Producto producto, InventarioService inventario, CancellationToken ct) =>
{
    try
    {
        if (await inventario.ObtenerAsync(producto.Id, ct) is not null)
        {
            return Results.Conflict(new { error = $"El producto '{producto.Id}' ya existe." });
        }

        var creado = await inventario.RegistrarAsync(producto, ct);
        return Results.Created($"/api/productos/{creado.Id}", creado);
    }
    catch (CatalogoException ex)
    {
        return Results.BadRequest(new { error = ex.Message, codigo = ex.Codigo });
    }
});

// ---------- API SOAP (interna, la consume el Servicio de Pedidos) ----------
app.MapCatalogoSoap();

// ---------- Inyección de fallas para la demo de disponibilidad ----------
app.MapGet("/admin/simulacion", (SimulacionFallas simulacion) => Results.Ok(simulacion.Estado()));
app.MapPost("/admin/simulacion", (ConfiguracionSimulacion config, SimulacionFallas simulacion) =>
{
    simulacion.Configurar(config.LatenciaMs, config.Fallar);
    app.Logger.LogWarning("Simulación de fallas: latencia {Latencia} ms, fallar={Fallar}", config.LatenciaMs, config.Fallar);
    return Results.Ok(simulacion.Estado());
});

app.Run();
