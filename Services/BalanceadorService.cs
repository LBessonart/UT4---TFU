using MaquetaApi.Components;

namespace MaquetaApi.Services;

public sealed class BalanceadorService
{
    private readonly IReadOnlyList<IPedidoService> _replicas;
    private int _siguienteReplica = -1;

    public BalanceadorService(IEnumerable<IPedidoService> replicas)
    {
        _replicas = replicas.ToArray();
        if (_replicas.Count == 0)
        {
            throw new InvalidOperationException("El balanceador necesita al menos una réplica de pedidos.");
        }
    }

    public IPedidoService ObtenerReplica()
    {
        var indice = (uint)Interlocked.Increment(ref _siguienteReplica) % (uint)_replicas.Count;
        return _replicas[(int)indice];
    }
}