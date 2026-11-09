using ECommerce.Domain.Data.DbContexts;
using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Seeding.Data;
using ECommerce.Infrastructure.Seeding.Data.Models;

namespace ECommerce.Infrastructure.Seeding
{
    public class ProductTypeSeeder(StoreDbContext dbContext) : IDataSeeder
    {
        public int Order => 2;
        public async Task SeedAsync(CancellationToken cancellationToken = default)
            => await JsonSeeder.SeedIfEmpty<ProductType, ProductTypeSeedModel>(dbContext.Types, "types.json", b => ProductType.Create(b.Id, b.Name).Value, cancellationToken);
    }
}
