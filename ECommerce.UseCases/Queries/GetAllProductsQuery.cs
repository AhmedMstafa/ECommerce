using ECommerce.UseCases.Products;
using ECommerce.UseCases.Products.Dtos;

namespace ECommerce.UseCases.Queries;

public class GetAllProductsQuery(IProductQueryService productQueryService)
{
    public async Task<IReadOnlyList<GetAllProductsResponse>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return await productQueryService.GetAllProductsAsync(cancellationToken);
    }
}
