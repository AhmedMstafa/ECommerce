using ECommerce.Domain.Common;
using ECommerce.UseCases.Products;
using ECommerce.UseCases.Products.Dtos;

namespace ECommerce.UseCases.Queries;

public class GetProductByIdQuery(IProductQueryService productQueryService)
{
    public async Task<Result<GetByIdProductResponse>> ExecuteAsync(
        Guid id, 
        CancellationToken cancellationToken = default)
    {
        var product = await productQueryService.GetProductByIdAsync(id, cancellationToken);

        if (product is null)
        {
            return ProductErrors.NotFound(id);
        }

        return product;
    }
}
