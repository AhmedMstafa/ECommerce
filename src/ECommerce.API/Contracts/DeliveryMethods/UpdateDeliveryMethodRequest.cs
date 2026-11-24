namespace ECommerce.API.Contracts.DeliveryMethods;

public sealed record UpdateDeliveryMethodRequest(string Name, string? Description, decimal Price);