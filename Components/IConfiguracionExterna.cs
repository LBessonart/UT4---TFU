namespace MaquetaApi.Components;

public interface IConfiguracionExterna
{
    Task<string?> ObtenerAsync(string clave, CancellationToken cancellationToken = default);
    Task<int> ObtenerEnteroAsync(string clave, int valorPorDefecto, CancellationToken cancellationToken = default);
}