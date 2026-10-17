using System.Net;
using System.Security.Claims;

namespace MaquetaApi.Middleware;

public sealed class GatekeeperMiddleware(RequestDelegate next, ILogger<GatekeeperMiddleware> logger)
{
    private static readonly HashSet<string> ValidApiKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "dev-key-12345",
        "prod-key-67890"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        // Saltar autenticación para endpoints públicos
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/debug", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // Verificar API Key en header
        if (!context.Request.Headers.TryGetValue("X-API-Key", out var apiKeyValues))
        {
            logger.LogWarning("Solicitud rechazada: header X-API-Key faltante desde {RemoteIP}", 
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "API Key requerida" });
            return;
        }

        var apiKey = apiKeyValues.FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey) || !ValidApiKeys.Contains(apiKey))
        {
            logger.LogWarning("Solicitud rechazada: API Key inválida desde {RemoteIP}", 
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "API Key inválida" });
            return;
        }

        // Agregar claims al usuario autenticado
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, apiKey),
            new(ClaimTypes.Role, apiKey.StartsWith("dev", StringComparison.OrdinalIgnoreCase) ? "Developer" : "Production")
        };
        
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey"));
        logger.LogInformation("Solicitud autorizada con API Key desde {RemoteIP}", 
            context.Connection.RemoteIpAddress);

        await next(context);
    }
}
