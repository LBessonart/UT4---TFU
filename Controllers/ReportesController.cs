using System.Diagnostics;
using MaquetaApi.Data;
using MaquetaApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MaquetaApi.Controllers;

[ApiController]
[Route("api/reportes")]
public sealed class ReportesController(AppDbContext db) : ControllerBase
{
    [HttpGet("ventas-por-producto")]
    public async Task<IActionResult> VentasPorProducto([FromQuery] bool directo = false, CancellationToken cancellationToken = default)
    {
        // directo=true recorre todos los ítems; si no, lee la vista ya calculada
        var consulta = directo
            ? db.Database.SqlQueryRaw<VentaPorProducto>(ConsultasReporte.VentasPorProducto)
            : db.Database.SqlQueryRaw<VentaPorProducto>("SELECT * FROM ventas_por_producto");

        var reloj = Stopwatch.StartNew();
        var ventas = await consulta.OrderByDescending(v => v.UnidadesVendidas).ToListAsync(cancellationToken);
        reloj.Stop();

        return Ok(new
        {
            origen = directo ? "tablas" : "vista",
            milisegundos = reloj.ElapsedMilliseconds,
            ventas
        });
    }
}