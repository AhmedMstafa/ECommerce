using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Persistence;
using ECommerce.Infrastructure.Seeding.Data;
using ECommerce.Infrastructure.Seeding.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.Seeding;

public sealed class DeliveryMethodSeeder(
    StoreDbContext dbContext,
    ILogger<DeliveryMethodSeeder> logger) : IDataSeeder
{
    public int Order => 3;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var models = await JsonSeeder.ReadAsync<DeliveryMethodSeedModel>(
            "delivery-methods.json",
            cancellationToken);

        if (models.Count == 0)
        {
            return;
        }

        var existing = await dbContext.DeliveryMethods
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        var existingIds = existing.Select(x => x.Id).ToHashSet();
        var existingNames = existing.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var utcNow = DateTime.UtcNow;

        foreach (var model in models)
        {
            if (existingIds.Contains(model.Id) || existingNames.Contains(model.Name))
            {
                continue;
            }

            var result = DeliveryMethod.Create(
                model.Name,
                model.Description,
                model.Price,
                utcNow,
                model.Id);

            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Delivery method {Name} was skipped: {Error}",
                    model.Name,
                    result.Error?.Message);
                continue;
            }

            dbContext.DeliveryMethods.Add(result.Value);
            existingIds.Add(model.Id);
            existingNames.Add(model.Name);
        }
    }
}
