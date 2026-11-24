namespace ECommerce.UseCases.DeliveryMethods.Dtos;

public sealed record DeliveryMethodResponse(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    bool IsActive);
