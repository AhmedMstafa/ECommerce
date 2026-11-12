using ECommerce.UseCases.Products.Dtos;

namespace ECommerce.UseCases.Products;

public interface IProductQueryService
{
    Task<IReadOnlyList<GetAllProductsResponse>> GetAllProductsAsync(CancellationToken cancellationToken = default);
    Task<GetByIdProductResponse?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
