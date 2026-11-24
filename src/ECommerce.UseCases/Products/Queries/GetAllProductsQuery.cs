using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Common.Caching;
using ECommerce.UseCases.Common.Pagination;
using ECommerce.UseCases.Products.Caching;
using ECommerce.UseCases.Products.Dtos;
using ECommerce.UseCases.Products.Specifications;
using Mapster;
using MediatR;

namespace ECommerce.UseCases.Products.Queries;

public record GetAllProductsQuery(ProductSpecificationParameters? Parameters = null)
    : IRequest<Result<PagedResponse<GetAllProductsResponse>>>;

public class GetAllProductsQueryHandler(
    IReadRepository<Product> productRepository,
    ICacheService cacheService)
    : IRequestHandler<GetAllProductsQuery, Result<PagedResponse<GetAllProductsResponse>>>
{
    public async Task<Result<PagedResponse<GetAllProductsResponse>>> Handle(
        GetAllProductsQuery request,
        CancellationToken cancellationToken)
    {
        var parameters = request.Parameters ?? new ProductSpecificationParameters();
        var cacheKey = ProductCacheKeys.List(parameters);

        var page = await cacheService.GetOrCreateAsync(
            cacheKey,
            ct => LoadPageAsync(parameters, ct),
            CacheExpiration.ProductList,
            cancellationToken);

        return Result.Success(page);
    }

    private async Task<PagedResponse<GetAllProductsResponse>> LoadPageAsync(
        ProductSpecificationParameters parameters,
        CancellationToken cancellationToken)
    {
        var pagedSpec = new ProductsWithBrandAndTypeSpecification(parameters);
        var countSpec = new ProductsCountSpecification(parameters);

        // The same DbContext is scoped per request, so these queries run in
        // sequence; EF Core does not support concurrent operations on one context.
        var products = await productRepository.GetAllWithSpecAsync(pagedSpec, cancellationToken);
        var totalCount = await productRepository.CountAsync(countSpec, cancellationToken);

        var items = products.Adapt<IReadOnlyList<GetAllProductsResponse>>();

        return new PagedResponse<GetAllProductsResponse>(
            items,
            parameters.PageNumber,
            parameters.PageSize,
            totalCount);
    }
}
