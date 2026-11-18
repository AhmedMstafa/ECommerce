using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Products.Dtos;
using ECommerce.UseCases.Products.Specifications;
using Mapster;

namespace ECommerce.UseCases.Queries;

public class GetAllProductsQuery(IReadRepository<Product> productRepository)
{
    public async Task<IReadOnlyList<GetAllProductsResponse>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(null, cancellationToken);
    }

    public async Task<IReadOnlyList<GetAllProductsResponse>> ExecuteAsync(
        ProductSpecificationParameters? parameters,
        CancellationToken cancellationToken = default)
    {
        var spec = parameters is not null
            ? new ProductsWithBrandAndTypeSpecification(parameters)
            : new ProductsWithBrandAndTypeSpecification();

        var products = await productRepository.GetAllWithSpecAsync(spec, cancellationToken);
        return products.Adapt<IReadOnlyList<GetAllProductsResponse>>();
    }
}
