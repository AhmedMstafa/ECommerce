using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Brands.Dtos;
using ECommerce.UseCases.Brands.Specifications;
using Mapster;

namespace ECommerce.UseCases.Queries;

public class GetAllBrandsQuery(IReadRepository<ProductBrand> brandRepository)
{
    public async Task<IReadOnlyList<GetAllBrandsResponse>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var spec = new GetAllBrandsSpecification();
        var brands = await brandRepository.GetAllWithSpecAsync(spec, cancellationToken);
        return brands.Adapt<IReadOnlyList<GetAllBrandsResponse>>();
    }
}
