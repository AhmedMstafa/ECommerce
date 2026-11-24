using ECommerce.Domain.Common;

namespace ECommerce.Domain.Entities
{
    /// <summary>
    /// Redis-stored basket line. Prices are only a snapshot for comparison;
    /// checkout and basket responses always read the current price from SQL.
    /// </summary>
    public sealed class BasketItem
    {
        private BasketItem() { }

        private BasketItem(
            Guid productId,
            int quantity,
            decimal? priceAtAdd,
            DateTime addedAt)
        {
            ProductId = productId;
            Quantity = quantity;
            PriceAtAdd = priceAtAdd;
            AddedAt = addedAt;
        }

        public Guid ProductId { get; private set; }

        public int Quantity { get; private set; }

        public decimal? PriceAtAdd { get; private set; }

        public DateTime AddedAt { get; private set; }

        public static Result<BasketItem> Create(
            Guid productId,
            int quantity,
            decimal? priceAtAdd,
            DateTime utcNow)
        {
            if (productId == Guid.Empty)
            {
                return Error.Validation("Basket.InvalidProductId", "A product id is required.");
            }

            if (quantity <= 0)
            {
                return BasketErrors.InvalidQuantity;
            }

            return new BasketItem(productId, quantity, priceAtAdd, utcNow);
        }

        public static BasketItem Rehydrate(
            Guid productId,
            int quantity,
            decimal? priceAtAdd,
            DateTime addedAt) => new(productId, quantity, priceAtAdd, addedAt);

        public Result IncreaseQuantity(int amount)
        {
            if (amount <= 0)
            {
                return BasketErrors.InvalidQuantity;
            }

            Quantity += amount;
            return Result.Success();
        }

        public Result SetQuantity(int quantity)
        {
            if (quantity <= 0)
            {
                return BasketErrors.InvalidQuantity;
            }

            Quantity = quantity;
            return Result.Success();
        }

        public void SetPriceAtAddIfMissing(decimal? priceAtAdd)
        {
            if (PriceAtAdd is null)
            {
                PriceAtAdd = priceAtAdd;
            }
        }
    }
}
