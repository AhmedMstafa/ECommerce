using ECommerce.Domain.Data.DbContexts;
using ECommerce.UseCases.Brands;
using ECommerce.UseCases.Brands.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Infrastructure.Queries;

public class BrandQueryService(StoreDbContext storeDbContext) : IBrandQueryService
{
    public async Task<IReadOnlyList<GetAllBrandsResponse>> GetAllBrandsAsync(CancellationToken cancellationToken = default)
    {
        return await storeDbContext.Brands
            .Select(b => new GetAllBrandsResponse
            {
                Id = b.Id,
                Name = b.Name
            })
            .ToListAsync(cancellationToken);
    }
}
