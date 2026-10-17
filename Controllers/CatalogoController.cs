using MaquetaApi.Components;
using MaquetaApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace MaquetaApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class CatalogoController(ICatalogo catalogo) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<Producto>>> ObtenerProductos(CancellationToken cancellationToken)
    {
        var productos = await catalogo.ObtenerProductosAsync(cancellationToken);
        return Ok(productos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Producto>> ObtenerProducto(string id, CancellationToken cancellationToken)
    {
        var producto = await catalogo.ObtenerProductoAsync(id, cancellationToken);
        if (producto is null)
        {
            return NotFound(new { error = $"Producto {id} no encontrado" });
        }
        return Ok(producto);
    }

    [HttpPost]
    public async Task<ActionResult<Producto>> RegistrarProducto([FromBody] Producto producto, CancellationToken cancellationToken)
    {
        var resultado = await catalogo.RegistrarProductoAsync(producto, cancellationToken);
        return CreatedAtAction(nameof(ObtenerProducto), new { id = resultado.Id }, resultado);
    }
}
