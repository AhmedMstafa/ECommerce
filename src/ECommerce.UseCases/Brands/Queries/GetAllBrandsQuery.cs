using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Brands.Dtos;
using ECommerce.UseCases.Brands.Specifications;
using ECommerce.UseCases.Common.Caching;
using Mapster;
using MediatR;

namespace ECommerce.UseCases.Brands.Queries;

public record GetAllBrandsQuery : IRequest<IReadOnlyList<GetAllBrandsResponse>>;

public class GetAllBrandsQueryHandler(
    IReadRepository<ProductBrand> brandRepository,
    ICacheService cacheService) 
    : IRequestHandler<GetAllBrandsQuery, IReadOnlyList<GetAllBrandsResponse>>
{
    private const string CacheKey = "brands_all";

    public async Task<IReadOnlyList<GetAllBrandsResponse>> Handle(
        GetAllBrandsQuery request, 
        CancellationToken cancellationToken)
    {
        return await cacheService.GetOrCreateAsync(
            CacheKey,
            async ct =>
            {
                var spec = new GetAllBrandsSpecification();
                var brands = await brandRepository.GetAllWithSpecAsync(spec, ct);
                return brands.Adapt<IReadOnlyList<GetAllBrandsResponse>>();
            },
            TimeSpan.FromMinutes(30),
            cancellationToken);
    }
}
