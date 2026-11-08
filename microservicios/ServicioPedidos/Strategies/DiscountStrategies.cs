namespace ServicioPedidos.Strategies;

// Patrón Strategy heredado de las TFU anteriores (RNF 3 y RNF 4): ahora vive en el Servicio de Pedidos,
// que es el único que necesita calcular totales.
public interface IDiscountStrategy
{
    decimal ApplyDiscount(decimal total);
}

public sealed class PercentageDiscountStrategy(decimal percentage = 0.10m) : IDiscountStrategy
{
    public decimal ApplyDiscount(decimal total) => total * (1 - percentage);
}

public sealed class FixedDiscountStrategy(decimal fixedAmount = 10.0m) : IDiscountStrategy
{
    public decimal ApplyDiscount(decimal total) => Math.Max(total - fixedAmount, 0);
}
