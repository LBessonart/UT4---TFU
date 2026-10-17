using MaquetaApi.Models;
using MaquetaApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MaquetaApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PedidoController : ControllerBase
    {
        private readonly IPedidoService _pedidoService;
        public PedidoController(IPedidoService pedidoService)
        {
            _pedidoService = pedidoService;
        }

        [HttpGet("rollbasico")]
        public async Task<ActionResult<Pedido>> RollBasico([FromQuery] bool simularError = false, CancellationToken cancellationToken = default)
        {
            // Crear pedido de prueba
            var pedido = new Pedido
            {
                ClienteId = "cliente-test-001",
                Items = new List<PedidoItem>
                {
                    new PedidoItem
                    {
                        ProductoId = "prod-001",
                        Nombre = "Producto A",
                        Cantidad = 2,
                        PrecioUnitario = 50.0m
                    },
                    new PedidoItem
                    {
                        ProductoId = "prod-002",
                        Nombre = "Producto B",
                        Cantidad = 1,
                        PrecioUnitario = 30.0m
                    }
                }
            };

            try
            {
                var pedidoProcesado = await _pedidoService.ProcesarPedidoAsync(pedido, simularError, cancellationToken);
                return Ok(pedidoProcesado);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

    }
}
