namespace MaquetaApi.Strategies
{
    public class FixedDiscountStrategy : IDiscountStrategy
    {
        private readonly decimal _fixedAmount;

        public FixedDiscountStrategy(decimal fixedAmount = 10.0m)
        {
            _fixedAmount = fixedAmount;
        }

        public decimal ApplyDiscount(decimal total)
        {
            var discounted = total - _fixedAmount;
            return discounted > 0 ? discounted : 0;
        }
    }
}
