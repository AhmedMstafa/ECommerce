using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Common.Caching;
using ECommerce.UseCases.Types.Dtos;
using ECommerce.UseCases.Types.Specifications;
using Mapster;
using MediatR;

namespace ECommerce.UseCases.Types.Queries;

public record GetAllTypesQuery : IRequest<IReadOnlyList<GetAllTypesResponse>>;

public class GetAllTypesQueryHandler(
    IReadRepository<ProductType> typeRepository,
    ICacheService cacheService) 
    : IRequestHandler<GetAllTypesQuery, IReadOnlyList<GetAllTypesResponse>>
{
    private const string CacheKey = "types_all";

    public async Task<IReadOnlyList<GetAllTypesResponse>> Handle(
        GetAllTypesQuery request, 
        CancellationToken cancellationToken)
    {
        return await cacheService.GetOrCreateAsync(
            CacheKey,
            async ct =>
            {
                var spec = new GetAllTypesSpecification();
                var types = await typeRepository.GetAllWithSpecAsync(spec, ct);
                return types.Adapt<IReadOnlyList<GetAllTypesResponse>>();
            },
            TimeSpan.FromMinutes(30),
            cancellationToken);
    }
}
