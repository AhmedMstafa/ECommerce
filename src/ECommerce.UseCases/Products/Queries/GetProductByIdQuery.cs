using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Common.Caching;
using ECommerce.UseCases.Products.Caching;
using ECommerce.UseCases.Products.Dtos;
using ECommerce.UseCases.Products.Specifications;
using Mapster;
using MediatR;

namespace ECommerce.UseCases.Products.Queries;

public record GetProductByIdQuery(Guid Id) : IRequest<Result<GetByIdProductResponse>>;

public class GetProductByIdQueryHandler(
    IReadRepository<Product> productRepository,
    ICacheService cacheService)
    : IRequestHandler<GetProductByIdQuery, Result<GetByIdProductResponse>>
{
    public async Task<Result<GetByIdProductResponse>> Handle(
        GetProductByIdQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = ProductCacheKeys.Details(request.Id);

        var product = await cacheService.GetOrCreateAsync<GetByIdProductResponse?>(
            cacheKey,
            async ct =>
            {
                var spec = new ProductByIdWithBrandAndTypeSpecification(request.Id);
                var entity = await productRepository.GetEntityWithSpecAsync(spec, ct);

                return entity?.Adapt<GetByIdProductResponse>();
            },
            CacheExpiration.ProductDetails,
            cancellationToken);

        if (product is null)
        {
            return ProductErrors.NotFound(request.Id);
        }

        return product;
    }
}
