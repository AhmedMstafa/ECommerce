using ECommerce.Domain.Data.DbContexts;
using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Seeding.Data;
using ECommerce.Infrastructure.Seeding.Data.Models;

namespace ECommerce.Infrastructure.Seeding
{
    public class ProductBrandSeeder(StoreDbContext dbContext) : IDataSeeder
    {
        public int Order => 1;
        public async Task SeedAsync(CancellationToken cancellationToken = default)
         => await JsonSeeder.SeedIfEmpty<ProductBrand, ProductBrandSeedModel>(dbContext.Brands, "brands.json", b => ProductBrand.Create(b.Id, b.Name).Value, cancellationToken);
    }
}
