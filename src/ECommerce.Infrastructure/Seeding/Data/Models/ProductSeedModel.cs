namespace ECommerce.Infrastructure.Seeding.Data.Models;

public sealed record ProductSeedModel(
    Guid Id,
    string Name,
    string Description,
    string PictureUrl,
    decimal Price,
    int StockQuantity,
    string Brand,
    string Type);
