namespace MaquetaApi.Components;

public interface IDescuento
{
    decimal Aplicar(decimal subtotal);
}