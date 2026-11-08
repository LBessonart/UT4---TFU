namespace ServicioPedidos.Integracion;

/// <summary>
/// Falla transitoria de una dependencia (5xx, SOAP Fault "Server", respuesta ilegible).
/// Es la que cuentan el Retry y el Circuit Breaker.
/// </summary>
public sealed class DependenciaTransitoriaException(string mensaje) : Exception(mensaje);

/// <summary>
/// Error de negocio del catálogo (SOAP Fault "Client"): stock insuficiente, producto inexistente...
/// NO se reintenta y NO abre el circuito: el catálogo respondió bien, la solicitud era inválida.
/// </summary>
public sealed class CatalogoFaultException(string codigo, string mensaje) : Exception(mensaje)
{
    public string Codigo { get; } = codigo;
}

/// <summary>
/// La dependencia no respondió a tiempo, siguió fallando tras los reintentos o tiene el circuito abierto.
/// Se traduce a HTTP 503 hacia el cliente.
/// </summary>
public sealed class DependenciaNoDisponibleException(string servicio, string motivo, int reintentarEnSegundos)
    : Exception($"El servicio de {servicio} no está disponible: {motivo}")
{
    public string Servicio { get; } = servicio;
    public string Motivo { get; } = motivo;
    public int ReintentarEnSegundos { get; } = reintentarEnSegundos;
}
