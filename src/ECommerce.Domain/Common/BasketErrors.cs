namespace ECommerce.Domain.Common
{
    public static class BasketErrors
    {
        public static readonly Error GuestTokenRequired =
            Error.Validation("Basket.GuestTokenRequired", "A valid guest token is required.");

        public static readonly Error InvalidQuantity =
            Error.Validation("Basket.InvalidQuantity", "Basket quantity must be positive.");

        public static readonly Error AuthenticationRequired =
            Error.Unauthorized("Basket.AuthenticationRequired", "An authenticated user is required.");

        public static Error ProductUnavailable(Guid productId) =>
            Error.NotFound(
                "Basket.ProductUnavailable",
                $"Product '{productId}' is not available.");

        public static Error InsufficientStock(Guid productId, int availableQuantity) =>
            Error.Conflict(
                "Basket.InsufficientStock",
                $"Only {availableQuantity} unit(s) of product '{productId}' are available.");

        public static Error MaximumItemsReached(int maximumItems) =>
            Error.Conflict(
                "Basket.MaximumItemsReached",
                $"The basket cannot contain more than {maximumItems} different products.");

        public static Error MaximumQuantityExceeded(int maximumQuantity) =>
            Error.Validation(
                "Basket.MaximumQuantityExceeded",
                $"The quantity for one product cannot exceed {maximumQuantity}.");

        public static Error ItemNotFound(Guid productId) =>
            Error.NotFound(
                "Basket.ItemNotFound",
                $"Product '{productId}' is not in the basket.");

        public static readonly Error ConcurrentUpdate =
            Error.Conflict(
                "Basket.ConcurrentUpdate",
                "The basket was changed by another request. Please retry.");

        public static readonly Error StorageUnavailable =
            Error.Failure(
                "Basket.StorageUnavailable",
                "The basket service is temporarily unavailable. Please retry.");
    }
}
