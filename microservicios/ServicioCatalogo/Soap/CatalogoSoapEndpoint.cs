using System.Xml;
using System.Xml.Linq;
using ServicioCatalogo.Models;
using ServicioCatalogo.Services;

namespace ServicioCatalogo.Soap;

/// <summary>
/// Endpoint SOAP 1.1 (document/literal) del catálogo.
///   GET  /ws/productos.wsdl  -> contrato WSDL
///   POST /ws/productos       -> GetProducto, ReservarStock, LiberarStock
/// Se implementa a mano con System.Xml.Linq para que el mensaje XML sea explícito
/// y no depender de un framework SOAP externo.
/// </summary>
public static class CatalogoSoapEndpoint
{
    public const string Ns = "http://ucu.edu.uy/tfu5/catalogo";
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Tns = Ns;
    private const string ContentType = "text/xml; charset=utf-8";

    public static void MapCatalogoSoap(this WebApplication app)
    {
        app.MapGet("/ws/productos.wsdl", (HttpContext ctx, IConfiguration config) =>
        {
            var ubicacion = config["Soap:Url"] ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}/ws/productos";
            return Results.Text(Wsdl.Generar(ubicacion), ContentType);
        });

        app.MapPost("/ws/productos", ProcesarAsync);
    }

    private static async Task<IResult> ProcesarAsync(
        HttpRequest request,
        InventarioService inventario,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("CatalogoSoap");

        XDocument documento;
        try
        {
            documento = await XDocument.LoadAsync(request.Body, LoadOptions.None, ct);
        }
        catch (XmlException ex)
        {
            return Fault("soap:Client", $"XML mal formado: {ex.Message}", CodigosError.SolicitudInvalida);
        }

        var operacion = documento.Root?.Name == Soap + "Envelope"
            ? documento.Root.Element(Soap + "Body")?.Elements().FirstOrDefault()
            : null;

        if (operacion is null)
        {
            return Fault("soap:Client", "Se esperaba un soap:Envelope con un soap:Body.", CodigosError.SolicitudInvalida);
        }

        try
        {
            XElement respuesta = operacion.Name switch
            {
                var n when n == Tns + "GetProductoRequest" => await GetProductoAsync(operacion, inventario, ct),
                var n when n == Tns + "ReservarStockRequest" => await ReservarStockAsync(operacion, inventario, ct),
                var n when n == Tns + "LiberarStockRequest" => await LiberarStockAsync(operacion, inventario, ct),
                _ => throw new CatalogoException(CodigosError.OperacionDesconocida, $"Operación desconocida: {operacion.Name}")
            };

            return Results.Text(Envelope(respuesta).ToString(SaveOptions.DisableFormatting), ContentType);
        }
        catch (CatalogoException ex)
        {
            return Fault("soap:Client", ex.Message, ex.Codigo);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Error procesando {Operacion}", operacion.Name.LocalName);
            return Fault("soap:Server", "Error interno del catálogo.", CodigosError.ErrorInterno);
        }
    }

    private static async Task<XElement> GetProductoAsync(XElement req, InventarioService inventario, CancellationToken ct)
    {
        var id = Texto(req, "id");
        var producto = await inventario.ObtenerAsync(id, ct)
            ?? throw new CatalogoException(CodigosError.ProductoNoEncontrado, $"El producto '{id}' no existe.");

        // El stock NO viaja en GetProducto: es un dato volátil que solo se confirma al reservar
        return new XElement(Tns + "GetProductoResponse",
            new XElement(Tns + "producto",
                new XElement(Tns + "id", producto.Id),
                new XElement(Tns + "nombre", producto.Nombre),
                new XElement(Tns + "precio", XmlConvert.ToString(producto.Precio))));
    }

    private static async Task<XElement> ReservarStockAsync(XElement req, InventarioService inventario, CancellationToken ct)
    {
        var pedidoId = LeerPedidoId(req);
        var items = req.Element(Tns + "items")?.Elements(Tns + "item")
            .Select(i => new ItemAReservar(Texto(i, "productoId"), Entero(i, "cantidad")))
            .ToList() ?? [];

        var resultado = await inventario.ReservarStockAsync(pedidoId, items, ct);

        return new XElement(Tns + "ReservarStockResponse",
            new XElement(Tns + "pedidoId", resultado.PedidoId),
            new XElement(Tns + "estado", resultado.Estado),
            new XElement(Tns + "yaProcesada", XmlConvert.ToString(resultado.YaProcesada)));
    }

    private static async Task<XElement> LiberarStockAsync(XElement req, InventarioService inventario, CancellationToken ct)
    {
        var pedidoId = LeerPedidoId(req);
        var estado = await inventario.LiberarStockAsync(pedidoId, ct);

        return new XElement(Tns + "LiberarStockResponse",
            new XElement(Tns + "pedidoId", pedidoId),
            new XElement(Tns + "estado", estado));
    }

    private static string Texto(XElement padre, string nombre)
    {
        var valor = padre.Element(Tns + nombre)?.Value.Trim();
        return string.IsNullOrEmpty(valor)
            ? throw new CatalogoException(CodigosError.SolicitudInvalida, $"Falta el elemento '{nombre}'.")
            : valor;
    }

    private static int Entero(XElement padre, string nombre) =>
        int.TryParse(Texto(padre, nombre), out var numero)
            ? numero
            : throw new CatalogoException(CodigosError.SolicitudInvalida, $"'{nombre}' debe ser un entero.");

    private static Guid LeerPedidoId(XElement padre) =>
        Guid.TryParse(Texto(padre, "pedidoId"), out var id)
            ? id
            : throw new CatalogoException(CodigosError.SolicitudInvalida, "'pedidoId' debe ser un GUID.");

    private static XDocument Envelope(XElement contenido) =>
        new(new XDeclaration("1.0", "utf-8", null),
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", Soap),
                new XAttribute(XNamespace.Xmlns + "cat", Tns),
                new XElement(Soap + "Body", contenido)));

    // SOAP 1.1: los Faults se devuelven con HTTP 500
    private static IResult Fault(string codigo, string mensaje, string codigoNegocio)
    {
        var fault = new XElement(Soap + "Fault",
            new XElement("faultcode", codigo),
            new XElement("faultstring", mensaje),
            new XElement("detail",
                new XElement(Tns + "CatalogoFault",
                    new XElement(Tns + "codigo", codigoNegocio))));

        return Results.Text(Envelope(fault).ToString(SaveOptions.DisableFormatting), ContentType, statusCode: StatusCodes.Status500InternalServerError);
    }
}
