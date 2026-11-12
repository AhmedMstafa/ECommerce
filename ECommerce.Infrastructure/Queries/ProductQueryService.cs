using ECommerce.Domain.Data.DbContexts;
using ECommerce.UseCases.Products;
using ECommerce.UseCases.Products.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Infrastructure.Queries
{
    public class ProductQueryService(StoreDbContext storeDbContext) : IProductQueryService
    {
        public async Task<IReadOnlyList<GetAllProductsResponse>> GetAllProductsAsync(CancellationToken cancellationToken = default)
        {
            return await storeDbContext.Products
                .Select(p => new GetAllProductsResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    PictureUrl = p.PictureUrl,
                    Price = p.Price,
                    ProductBrand = p.Brand.Name,
                    ProductType = p.Type.Name
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<GetByIdProductResponse?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await storeDbContext.Products
                .Where(p => p.Id == id)
                .Select(p => new GetByIdProductResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    PictureUrl = p.PictureUrl,
                    Price = p.Price,
                    ProductBrand = p.Brand.Name,
                    ProductType = p.Type.Name
                })
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
