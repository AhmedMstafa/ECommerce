using ECommerce.UseCases.Brands;
using ECommerce.UseCases.Brands.Dtos;

namespace ECommerce.UseCases.Queries;

public class GetAllBrandsQuery(IBrandQueryService brandQueryService)
{
    public async Task<IReadOnlyList<GetAllBrandsResponse>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return await brandQueryService.GetAllBrandsAsync(cancellationToken);
    }
}
