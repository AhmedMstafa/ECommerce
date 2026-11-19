using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Common.Pagination;
using ECommerce.UseCases.Products.Dtos;
using ECommerce.UseCases.Products.Specifications;
using Mapster;
using MediatR;

namespace ECommerce.UseCases.Products.Queries;

public record GetAllProductsQuery(ProductSpecificationParameters? Parameters = null)
    : IRequest<Result<PagedResponse<GetAllProductsResponse>>>;

public class GetAllProductsQueryHandler(IReadRepository<Product> productRepository)
    : IRequestHandler<GetAllProductsQuery, Result<PagedResponse<GetAllProductsResponse>>>
{
    public async Task<Result<PagedResponse<GetAllProductsResponse>>> Handle(
        GetAllProductsQuery request,
        CancellationToken cancellationToken)
    {
        var parameters = request.Parameters ?? new ProductSpecificationParameters();

        var pagedSpec = new ProductsWithBrandAndTypeSpecification(parameters);
        var countSpec = new ProductsCountSpecification(parameters);

        // The same DbContext is scoped per request, so these queries run in
        // sequence; EF Core does not support concurrent operations on one context.
        var products = await productRepository.GetAllWithSpecAsync(pagedSpec, cancellationToken);
        var totalCount = await productRepository.CountAsync(countSpec, cancellationToken);

        var items = products.Adapt<IReadOnlyList<GetAllProductsResponse>>();
        var page = new PagedResponse<GetAllProductsResponse>(
            items,
            parameters.PageIndex,
            parameters.PageSize,
            totalCount);

        return Result.Success(page);
    }
}
