using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Types.Dtos;
using ECommerce.UseCases.Types.Specifications;
using Mapster;

namespace ECommerce.UseCases.Queries;

public class GetAllTypesQuery(IReadRepository<ProductType> typeRepository)
{
    public async Task<IReadOnlyList<GetAllTypesResponse>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var spec = new GetAllTypesSpecification();
        var types = await typeRepository.GetAllWithSpecAsync(spec, cancellationToken);
        return types.Adapt<IReadOnlyList<GetAllTypesResponse>>();
    }
}
