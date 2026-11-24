namespace ECommerce.UseCases.Orders.Dtos;

public sealed record OrderItemResponse(
    Guid ProductId,
    string ProductName,
    string PictureUrl,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);
