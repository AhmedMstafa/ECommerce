using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Persistence;
using ECommerce.Infrastructure.Seeding.Data;
using ECommerce.Infrastructure.Seeding.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.Seeding;

/// <summary>
/// Seeds a small demo catalog. Brands and types are resolved by name so the
/// seeder also works when those tables already contain operator-created rows.
/// </summary>
public sealed class ProductSeeder(
    StoreDbContext dbContext,
    ILogger<ProductSeeder> logger) : IDataSeeder
{
    public int Order => 4;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var models = await JsonSeeder.ReadAsync<ProductSeedModel>("products.json", cancellationToken);

        if (models.Count == 0)
        {
            return;
        }

        var brands = (await dbContext.Brands
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Id, StringComparer.OrdinalIgnoreCase);

        var types = (await dbContext.Types
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Id, StringComparer.OrdinalIgnoreCase);

        // IgnoreQueryFilters protects against re-adding a fixed id that was soft-deleted.
        var existing = await dbContext.Products
            .IgnoreQueryFilters()
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        var existingIds = existing.Select(x => x.Id).ToHashSet();
        var existingNames = existing.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var model in models)
        {
            if (existingIds.Contains(model.Id) || existingNames.Contains(model.Name))
            {
                continue;
            }

            if (!brands.TryGetValue(model.Brand, out var brandId))
            {
                logger.LogWarning(
                    "Product {Name} was skipped because brand {Brand} was not found.",
                    model.Name,
                    model.Brand);
                continue;
            }

            if (!types.TryGetValue(model.Type, out var typeId))
            {
                logger.LogWarning(
                    "Product {Name} was skipped because type {Type} was not found.",
                    model.Name,
                    model.Type);
                continue;
            }

            var result = Product.Create(
                model.Name,
                model.Description,
                model.PictureUrl,
                model.Price,
                brandId,
                typeId,
                model.Id,
                model.StockQuantity);

            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Product {Name} was skipped: {Error}",
                    model.Name,
                    result.Error?.Message);
                continue;
            }

            dbContext.Products.Add(result.Value);
            existingIds.Add(model.Id);
            existingNames.Add(model.Name);
        }
    }
}
