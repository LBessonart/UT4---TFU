namespace MaquetaApi.Data;

public static class ConsultasReporte
{
    // Una sola consulta, usada tanto para crear la vista como para el reporte "directo",
    // así se puede comprobar que las dos formas dan exactamente los mismos números.
    public const string VentasPorProducto = """
        SELECT i."ProductoId"                         AS "ProductoId",
               MAX(i."Nombre")                        AS "Nombre",
               SUM(i."Cantidad")                      AS "UnidadesVendidas",
               SUM(i."Cantidad" * i."PrecioUnitario") AS "Ingresos"
        FROM "PedidoItems" i
        GROUP BY i."ProductoId"
        """;
}