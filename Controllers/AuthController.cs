using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace MaquetaApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    [HttpGet("info")]
    public IActionResult ObtenerInfoAutenticacion()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "desconocido";
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "sin rol";
        
        return Ok(new
        {
            autenticado = User.Identity?.IsAuthenticated ?? false,
            userId,
            role,
            authenticationType = User.Identity?.AuthenticationType
        });
    }
}
