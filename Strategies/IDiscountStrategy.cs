using MaquetaApi.Components;

namespace MaquetaApi.Strategies
{
    public interface IDiscountStrategy : IDescuento
    {
        decimal ApplyDiscount(decimal total);

        decimal IDescuento.Aplicar(decimal subtotal) => ApplyDiscount(subtotal);
    }
}
