using MaquetaApi.Components;
using MaquetaApi.Models;
using MaquetaApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MaquetaApi.Controllers;

[ApiController]
[Route("api")]
public sealed class BalanceadorController : ControllerBase
{
    private readonly BalanceadorService _balanceador;
    private readonly ICatalogo _catalogo;
    private readonly IClientes _clientes;
    private readonly IPersistencia _persistencia;

    public BalanceadorController(BalanceadorService balanceador, ICatalogo catalogo, IClientes clientes, IPersistencia persistencia)
    {
        _balanceador = balanceador;
        _catalogo = catalogo;
        _clientes = clientes;
        _persistencia = persistencia;
    }

    [HttpGet("productos")]
    public async Task<ActionResult<IReadOnlyCollection<Producto>>> ObtenerProductos(CancellationToken cancellationToken) =>
        Ok(await _catalogo.ObtenerProductosAsync(cancellationToken));

    [HttpPost("productos")]
    public async Task<ActionResult<Producto>> RegistrarProducto([FromBody] Producto producto, CancellationToken cancellationToken)
    {
        var productoRegistrado = await _catalogo.RegistrarProductoAsync(producto, cancellationToken);
        return Created($"/api/productos/{productoRegistrado.Id}", productoRegistrado);
    }

    [HttpGet("clientes")]
    public async Task<ActionResult<IReadOnlyCollection<Cliente>>> ObtenerClientes(CancellationToken cancellationToken) =>
        Ok(await _clientes.ObtenerClientesAsync(cancellationToken));

    [HttpPost("clientes")]
    public async Task<ActionResult<Cliente>> RegistrarCliente([FromBody] Cliente cliente, CancellationToken cancellationToken)
    {
        var clienteRegistrado = await _clientes.RegistrarClienteAsync(cliente, cancellationToken);
        return Created($"/api/clientes/{clienteRegistrado.Id}", clienteRegistrado);
    }

    [HttpGet("pedidos")]
    public async Task<ActionResult<IReadOnlyCollection<Pedido>>> ObtenerPedidos(CancellationToken cancellationToken) =>
        Ok(await _persistencia.ObtenerPedidosAsync(cancellationToken));

    [HttpPost("pedidos")]
    public async Task<ActionResult<Pedido>> CrearPedido([FromBody] Pedido pedido, [FromQuery] bool simularError = false, CancellationToken cancellationToken = default)
    {
        var cliente = await _clientes.ObtenerClienteAsync(pedido.ClienteId, cancellationToken);
        if (cliente is null)
        {
            return NotFound(new { error = $"El cliente '{pedido.ClienteId}' no existe." });
        }

        try
        {
            var pedidoProcesado = await _balanceador.ObtenerReplica().ProcesarPedidoAsync(pedido, simularError, cancellationToken);
            return Ok(pedidoProcesado);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}