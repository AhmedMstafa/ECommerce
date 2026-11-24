namespace ECommerce.API.Contracts.Basket;

public sealed record AddBasketItemRequest(Guid ProductId, int Quantity);
