using Microsoft.EntityFrameworkCore;
using ServicioPedidos.Data;
using ServicioPedidos.Integracion;
using ServicioPedidos.Models;
using ServicioPedidos.Services;
using ServicioPedidos.Strategies;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Base de datos propia (Database per Service) ----------
var connectionString = config.GetConnectionString("Pedidos")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:Pedidos.");
builder.Services.AddDbContext<PedidosDbContext>(o => o.UseNpgsql(connectionString));

// ---------- Estrategia de descuento (RNF 3: binding en tiempo de configuración) ----------
switch (config["DiscountStrategy"]?.ToLowerInvariant() ?? "percentage")
{
    case "percentage":
        builder.Services.AddSingleton<IDiscountStrategy, PercentageDiscountStrategy>(_ => new PercentageDiscountStrategy());
        break;
    case "fixed":
        builder.Services.AddSingleton<IDiscountStrategy, FixedDiscountStrategy>(_ => new FixedDiscountStrategy());
        break;
    default:
        throw new ArgumentException($"Estrategia de descuento no reconocida: {config["DiscountStrategy"]}");
}

// ---------- Contenedor de caché (Cache-Aside) ----------
var redis = config.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redis))
{
    // Sin Redis configurado (ejecución local) se usa una caché en memoria con la misma interfaz
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.Services.AddStackExchangeRedisCache(o =>
    {
        var opciones = ConfigurationOptions.Parse(redis);
        opciones.AbortOnConnectFail = false; // arrancar aunque la caché esté caída
        opciones.ConnectTimeout = 1000;
        opciones.SyncTimeout = 1000;
        opciones.AsyncTimeout = 1000;
        o.ConfigurationOptions = opciones;
        o.InstanceName = "pedidos:";
    });
}

// ---------- Integración con otros servicios (Timeout + Retry + Circuit Breaker) ----------
builder.Services.AddSingleton<PoliticasResiliencia>();
builder.Services.AddHttpClient<ProductSoapClient>(c =>
{
    c.BaseAddress = new Uri(config["Servicios:CatalogoUrl"] ?? "http://localhost:5101");
    c.Timeout = TimeSpan.FromSeconds(30); // red de seguridad: el timeout real lo pone Polly por intento
});
builder.Services.AddHttpClient<ClientesClient>(c =>
{
    c.BaseAddress = new Uri(config["Servicios:ClientesUrl"] ?? "http://localhost:5102");
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<CatalogoCache>();
builder.Services.AddScoped<PedidoService>();

var app = builder.Build();

await BaseDeDatos.InicializarAsync(app.Services, app.Logger);

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Servicio"] = "pedidos";
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", servicio = "pedidos" }));

app.MapPost("/api/pedidos", async (CrearPedidoRequest solicitud, PedidoService pedidos, HttpContext http, CancellationToken ct) =>
{
    try
    {
        var resultado = await pedidos.CrearAsync(solicitud, ct);
        http.Response.Headers["X-Cache-Productos"] = $"hits={resultado.CacheHits}; misses={resultado.CacheMisses}";

        if (!resultado.Creado)
        {
            http.Response.Headers["X-Pedido-Existente"] = "true";
            return Results.Ok(resultado.Pedido);
        }

        return Results.Created($"/api/pedidos/{resultado.Pedido.Id}", resultado.Pedido);
    }
    catch (PedidoException ex)
    {
        return Results.Json(new { error = ex.Message, codigo = ex.Codigo }, statusCode: ex.Status);
    }
    catch (DependenciaNoDisponibleException ex)
    {
        // Fail fast: 503 + Retry-After en vez de dejar al cliente esperando
        http.Response.Headers.RetryAfter = ex.ReintentarEnSegundos.ToString();
        return Results.Json(new
        {
            error = ex.Message,
            codigo = "ServicioNoDisponible",
            servicio = ex.Servicio,
            motivo = ex.Motivo,
            sugerencia = "Reintente más tarde. Si envía el mismo 'id' de pedido, el reintento no duplica el pedido ni el stock."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/pedidos", async (PedidoService pedidos, CancellationToken ct) =>
    Results.Ok(await pedidos.ListarAsync(ct)));

app.MapGet("/api/pedidos/{id:guid}", async (Guid id, PedidoService pedidos, CancellationToken ct) =>
    await pedidos.BuscarAsync(id, ct) is { } pedido
        ? Results.Ok(pedido)
        : Results.NotFound(new { error = $"El pedido '{id}' no existe." }));

// Estado del Circuit Breaker hacia Catálogo (Closed / Open / HalfOpen), útil para la demo
app.MapGet("/api/pedidos/circuito", (PoliticasResiliencia politicas) =>
    Results.Ok(new { catalogo = politicas.EstadoCircuitoCatalogo.CircuitState.ToString() }));

app.Run();
