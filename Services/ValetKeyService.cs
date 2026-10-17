using System.Security.Cryptography;
using System.Text.Json;

namespace MaquetaApi.Services;

public sealed class ValetKeyService(ILogger<ValetKeyService> logger)
{
    private static readonly Dictionary<string, ValetKeyToken> ActiveTokens = new();
    private static readonly object Lock = new();

    public ValetKeyToken GenerarToken(string recursoId, string permiso, TimeSpan expiracion)
    {
        var tokenId = GenerarTokenId();
        var token = new ValetKeyToken
        {
            TokenId = tokenId,
            RecursoId = recursoId,
            Permiso = permiso,
            CreadoEn = DateTime.UtcNow,
            ExpiraEn = DateTime.UtcNow.Add(expiracion),
            UsosRestantes = 1
        };

        lock (Lock)
        {
            ActiveTokens[tokenId] = token;
        }

        logger.LogInformation("Valet Key generado: {TokenId} para recurso {RecursoId} con permiso {Permiso}", 
            tokenId, recursoId, permiso);
        return token;
    }

    public bool ValidarToken(string tokenId, string recursoId, string permisoRequerido)
    {
        lock (Lock)
        {
            if (!ActiveTokens.TryGetValue(tokenId, out var token))
            {
                logger.LogWarning("Valet Key {TokenId} no encontrado", tokenId);
                return false;
            }

            // Verificar expiración
            if (DateTime.UtcNow > token.ExpiraEn)
            {
                logger.LogWarning("Valet Key {TokenId} expirado", tokenId);
                ActiveTokens.Remove(tokenId);
                return false;
            }

            // Verificar recurso
            if (token.RecursoId != recursoId)
            {
                logger.LogWarning("Valet Key {TokenId} no autorizado para recurso {RecursoId}", tokenId, recursoId);
                return false;
            }

            // Verificar permiso
            if (token.Permiso != permisoRequerido)
            {
                logger.LogWarning("Valet Key {TokenId} no tiene permiso {Permiso}", tokenId, permisoRequerido);
                return false;
            }

            // Verificar usos restantes
            if (token.UsosRestantes <= 0)
            {
                logger.LogWarning("Valet Key {TokenId} sin usos restantes", tokenId);
                ActiveTokens.Remove(tokenId);
                return false;
            }

            // Decrementar usos
            token.UsosRestantes--;
            if (token.UsosRestantes == 0)
            {
                ActiveTokens.Remove(tokenId);
                logger.LogInformation("Valet Key {TokenId} consumido y eliminado", tokenId);
            }

            logger.LogInformation("Valet Key {TokenId} validado exitosamente", tokenId);
            return true;
        }
    }

    public void RevocarToken(string tokenId)
    {
        lock (Lock)
        {
            if (ActiveTokens.Remove(tokenId))
            {
                logger.LogInformation("Valet Key {TokenId} revocado", tokenId);
            }
        }
    }

    private static string GenerarTokenId()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed class ValetKeyToken
{
    public string TokenId { get; set; } = string.Empty;
    public string RecursoId { get; set; } = string.Empty;
    public string Permiso { get; set; } = string.Empty;
    public DateTime CreadoEn { get; set; }
    public DateTime ExpiraEn { get; set; }
    public int UsosRestantes { get; set; }
}
