using Polly.Timeout;

namespace ServicioPedidos.Integracion;

/// <summary>Cliente REST/JSON del Servicio de Clientes: ValidarCliente(id). Timeout + Retry.</summary>
public sealed class ClientesClient(HttpClient http, PoliticasResiliencia politicas)
{
    public async Task<bool> ValidarAsync(string clienteId, CancellationToken ct)
    {
        try
        {
            return await politicas.Clientes.ExecuteAsync<bool>(async token =>
            {
                using var response = await http.GetAsync($"/api/clientes/{Uri.EscapeDataString(clienteId)}/validacion", token);

                if ((int)response.StatusCode >= 500)
                {
                    throw new DependenciaTransitoriaException($"Clientes respondió HTTP {(int)response.StatusCode}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    // Un 4xx no es transitorio: no tiene sentido reintentarlo
                    throw new InvalidOperationException($"Clientes respondió HTTP {(int)response.StatusCode}");
                }

                var validacion = await response.Content.ReadFromJsonAsync<RespuestaValidacion>(token);
                return validacion?.Valido ?? false;
            }, ct);
        }
        catch (TimeoutRejectedException)
        {
            throw new DependenciaNoDisponibleException("clientes", "no respondió dentro del tiempo límite", 5);
        }
        catch (HttpRequestException ex)
        {
            throw new DependenciaNoDisponibleException("clientes", $"error de red: {ex.Message}", 5);
        }
        catch (DependenciaTransitoriaException ex)
        {
            throw new DependenciaNoDisponibleException("clientes", ex.Message, 5);
        }
    }

    private sealed record RespuestaValidacion(string ClienteId, bool Valido);
}
