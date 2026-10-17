using MaquetaApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MaquetaApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ValetKeyController(ValetKeyService valetKeyService) : ControllerBase
{
    [HttpPost("generar")]
    public IActionResult GenerarToken([FromBody] GenerarTokenRequest request)
    {
        var expiracion = TimeSpan.FromMinutes(request.MinutosExpiracion);
        var token = valetKeyService.GenerarToken(request.RecursoId, request.Permiso, expiracion);
        return Ok(new
        {
            token.TokenId,
            token.RecursoId,
            token.Permiso,
            token.ExpiraEn,
            token.UsosRestantes
        });
    }

    [HttpPost("validar")]
    public IActionResult ValidarToken([FromBody] ValidarTokenRequest request)
    {
        var valido = valetKeyService.ValidarToken(request.TokenId, request.RecursoId, request.Permiso);
        return Ok(new { valido });
    }

    [HttpDelete("{tokenId}")]
    public IActionResult RevocarToken(string tokenId)
    {
        valetKeyService.RevocarToken(tokenId);
        return Ok(new { mensaje = $"Token {tokenId} revocado" });
    }

    [HttpGet("demo-acceso")]
    public IActionResult DemoAccesoValetKey(string tokenId, string recursoId, string permiso)
    {
        var valido = valetKeyService.ValidarToken(tokenId, recursoId, permiso);
        if (!valido)
        {
            return Unauthorized(new { error = "Token inválido, expirado o sin permisos" });
        }

        return Ok(new 
        { 
            mensaje = $"Acceso concedido a recurso {recursoId} con permiso {permiso}",
            timestamp = DateTime.UtcNow
        });
    }
}

public sealed class GenerarTokenRequest
{
    public string RecursoId { get; set; } = string.Empty;
    public string Permiso { get; set; } = string.Empty;
    public int MinutosExpiracion { get; set; } = 5;
}

public sealed class ValidarTokenRequest
{
    public string TokenId { get; set; } = string.Empty;
    public string RecursoId { get; set; } = string.Empty;
    public string Permiso { get; set; } = string.Empty;
}
