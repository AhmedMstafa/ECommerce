namespace ECommerce.Infrastructure.Seeding.Data.Models;

public sealed record DeliveryMethodSeedModel(
    Guid Id,
    string Name,
    string? Description,
    decimal Price);
