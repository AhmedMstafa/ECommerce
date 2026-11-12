using ECommerce.UseCases.Types;
using ECommerce.UseCases.Types.Dtos;

namespace ECommerce.UseCases.Queries;

public class GetAllTypesQuery(ITypeQueryService typeQueryService)
{
    public async Task<IReadOnlyList<GetAllTypesResponse>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return await typeQueryService.GetAllTypesAsync(cancellationToken);
    }
}
