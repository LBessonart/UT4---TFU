using System.Text;
using System.Xml;
using System.Xml.Linq;
using Polly.CircuitBreaker;
using Polly.Timeout;
using ServicioPedidos.Models;

namespace ServicioPedidos.Integracion;

/// <summary>
/// Cliente SOAP 1.1 del Servicio de Catálogo (ProductSoapClient en el diagrama de componentes).
/// Toda llamada pasa por el pipeline de resiliencia del catálogo (Timeout + Retry + Circuit Breaker).
/// </summary>
public sealed class ProductSoapClient(HttpClient http, PoliticasResiliencia politicas, ILogger<ProductSoapClient> logger)
{
    private const string Ns = "http://ucu.edu.uy/tfu5/catalogo";
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Tns = Ns;

    public async Task<ProductoInfo?> GetProductoAsync(string id, CancellationToken ct)
    {
        try
        {
            var respuesta = await InvocarAsync("GetProducto",
                new XElement(Tns + "GetProductoRequest", new XElement(Tns + "id", id)), ct);

            var producto = respuesta.Element(Tns + "producto")
                ?? throw new DependenciaTransitoriaException("GetProductoResponse sin <producto>");

            return new ProductoInfo(
                producto.Element(Tns + "id")!.Value,
                producto.Element(Tns + "nombre")!.Value,
                XmlConvert.ToDecimal(producto.Element(Tns + "precio")!.Value));
        }
        catch (CatalogoFaultException ex) when (ex.Codigo == "ProductoNoEncontrado")
        {
            return null;
        }
    }

    /// <summary>Idempotente: el pedidoId identifica la reserva, así que el Retry no descuenta dos veces.</summary>
    public async Task<(string Estado, bool YaProcesada)> ReservarStockAsync(Guid pedidoId, IEnumerable<ItemSolicitado> items, CancellationToken ct)
    {
        var respuesta = await InvocarAsync("ReservarStock",
            new XElement(Tns + "ReservarStockRequest",
                new XElement(Tns + "pedidoId", pedidoId),
                new XElement(Tns + "items",
                    items.Select(i => new XElement(Tns + "item",
                        new XElement(Tns + "productoId", i.ProductoId),
                        new XElement(Tns + "cantidad", i.Cantidad))))),
            ct);

        return (
            respuesta.Element(Tns + "estado")?.Value ?? "Desconocido",
            XmlConvert.ToBoolean(respuesta.Element(Tns + "yaProcesada")?.Value ?? "false"));
    }

    /// <summary>Compensación: devuelve el stock reservado. Idempotente.</summary>
    public async Task<string> LiberarStockAsync(Guid pedidoId, CancellationToken ct)
    {
        var respuesta = await InvocarAsync("LiberarStock",
            new XElement(Tns + "LiberarStockRequest", new XElement(Tns + "pedidoId", pedidoId)), ct);
        return respuesta.Element(Tns + "estado")?.Value ?? "Desconocido";
    }

    private async Task<XElement> InvocarAsync(string operacion, XElement cuerpo, CancellationToken ct)
    {
        try
        {
            return await politicas.Catalogo.ExecuteAsync<XElement>(async token =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/ws/productos")
                {
                    Content = new StringContent(Envelope(cuerpo), Encoding.UTF8, "text/xml")
                };
                request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{Ns}/{operacion}\"");

                using var response = await http.SendAsync(request, token);
                var texto = await response.Content.ReadAsStringAsync(token);

                XDocument documento;
                try
                {
                    documento = XDocument.Parse(texto);
                }
                catch (XmlException)
                {
                    throw new DependenciaTransitoriaException($"Catálogo respondió HTTP {(int)response.StatusCode} sin un mensaje SOAP válido");
                }

                var body = documento.Root?.Element(Soap + "Body")
                    ?? throw new DependenciaTransitoriaException("Respuesta SOAP sin soap:Body");

                if (body.Element(Soap + "Fault") is { } fault)
                {
                    var faultcode = fault.Element("faultcode")?.Value ?? string.Empty;
                    var mensaje = fault.Element("faultstring")?.Value ?? "SOAP Fault";
                    var codigo = fault.Element("detail")?.Element(Tns + "CatalogoFault")?.Element(Tns + "codigo")?.Value ?? "Desconocido";

                    // Fault "Client" = error de negocio (no se reintenta, no abre el circuito)
                    if (faultcode.EndsWith("Client", StringComparison.Ordinal))
                    {
                        throw new CatalogoFaultException(codigo, mensaje);
                    }

                    throw new DependenciaTransitoriaException($"SOAP Fault del servidor: {mensaje}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new DependenciaTransitoriaException($"Catálogo respondió HTTP {(int)response.StatusCode}");
                }

                return body.Elements().FirstOrDefault()
                    ?? throw new DependenciaTransitoriaException("Respuesta SOAP vacía");
            }, ct);
        }
        catch (BrokenCircuitException)
        {
            logger.LogWarning("{Operacion}: circuito abierto, se falla rápido sin llamar a Catálogo", operacion);
            throw new DependenciaNoDisponibleException("catálogo", "circuito abierto (fail fast)",
                (int)politicas.DuracionCircuitoAbierto.TotalSeconds);
        }
        catch (TimeoutRejectedException)
        {
            throw new DependenciaNoDisponibleException("catálogo", "no respondió dentro del tiempo límite", 5);
        }
        catch (HttpRequestException ex)
        {
            throw new DependenciaNoDisponibleException("catálogo", $"error de red: {ex.Message}", 5);
        }
        catch (DependenciaTransitoriaException ex)
        {
            throw new DependenciaNoDisponibleException("catálogo", ex.Message, 5);
        }
    }

    private static string Envelope(XElement cuerpo) =>
        new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soap", Soap),
            new XAttribute(XNamespace.Xmlns + "cat", Tns),
            new XElement(Soap + "Body", cuerpo)).ToString(SaveOptions.DisableFormatting);
}
