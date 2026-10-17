namespace MaquetaApi.Strategies
{
    public class PercentageDiscountStrategy : IDiscountStrategy
    {
        private readonly decimal _percentage;

        public PercentageDiscountStrategy(decimal percentage = 0.10m)
        {
            _percentage = percentage;
        }

        public decimal ApplyDiscount(decimal total)
        {
            return total * (1 - _percentage);
        }
    }
}
