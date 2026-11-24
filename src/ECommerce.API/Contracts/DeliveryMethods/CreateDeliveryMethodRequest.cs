namespace ECommerce.API.Contracts.DeliveryMethods;

public sealed record CreateDeliveryMethodRequest(string Name, string? Description, decimal Price);