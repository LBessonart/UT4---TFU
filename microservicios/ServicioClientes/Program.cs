using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Clientes")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:Clientes.");

// Base de datos propia del servicio (Database per Service)
builder.Services.AddDbContext<ClientesDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddScoped<ClienteService>();

var app = builder.Build();

await InicializarBaseDeDatosAsync(app.Services, app.Logger);

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Servicio"] = "clientes";
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", servicio = "clientes" }));

app.MapGet("/api/clientes", async (ClienteService clientes, CancellationToken ct) =>
    Results.Ok(await clientes.ListarAsync(ct)));

app.MapGet("/api/clientes/{id}", async (string id, ClienteService clientes, CancellationToken ct) =>
    await clientes.ObtenerAsync(id, ct) is { } cliente
        ? Results.Ok(cliente)
        : Results.NotFound(new { error = $"El cliente '{id}' no existe." }));

// ValidarCliente(id): contrato REST/JSON que consume el Servicio de Pedidos
app.MapGet("/api/clientes/{id}/validacion", async (string id, ClienteService clientes, CancellationToken ct) =>
    Results.Ok(new { clienteId = id, valido = await clientes.ValidarAsync(id, ct) }));

app.MapPost("/api/clientes", async (Cliente cliente, ClienteService clientes, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(cliente.Id) || string.IsNullOrWhiteSpace(cliente.Nombre))
    {
        return Results.BadRequest(new { error = "Id y nombre son obligatorios." });
    }

    if (await clientes.ObtenerAsync(cliente.Id, ct) is not null)
    {
        return Results.Conflict(new { error = $"El cliente '{cliente.Id}' ya existe." });
    }

    var creado = await clientes.RegistrarAsync(cliente, ct);
    return Results.Created($"/api/clientes/{creado.Id}", creado);
});

app.Run();

static async Task InicializarBaseDeDatosAsync(IServiceProvider services, ILogger logger)
{
    for (var intento = 1; ; intento++)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ClientesDbContext>();
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Clientes" ("Id", "Nombre", "Email") VALUES
                ('cliente-test-001', 'Cliente de prueba', 'cliente@example.com'),
                ('cliente-test-002', 'Segundo cliente', 'cliente2@example.com')
                ON CONFLICT ("Id") DO NOTHING;
                """);
            return;
        }
        catch (Exception ex) when (intento < 15)
        {
            logger.LogWarning("BD de clientes no disponible (intento {Intento}): {Error}", intento, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

public sealed class Cliente
{
    public string Id { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class ClientesDbContext(DbContextOptions<ClientesDbContext> options) : DbContext(options)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Cliente>(e =>
        {
            e.ToTable("Clientes");
            e.HasKey(c => c.Id);
        });
}

/// <summary>IClienteService del diagrama de componentes.</summary>
public sealed class ClienteService(ClientesDbContext db)
{
    public async Task<IReadOnlyCollection<Cliente>> ListarAsync(CancellationToken ct) =>
        await db.Clientes.AsNoTracking().OrderBy(c => c.Id).ToArrayAsync(ct);

    public Task<Cliente?> ObtenerAsync(string id, CancellationToken ct) =>
        db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<bool> ValidarAsync(string id, CancellationToken ct) =>
        db.Clientes.AsNoTracking().AnyAsync(c => c.Id == id, ct);

    public async Task<Cliente> RegistrarAsync(Cliente cliente, CancellationToken ct)
    {
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync(ct);
        return cliente;
    }
}
