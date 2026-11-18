using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Products.Dtos;
using ECommerce.UseCases.Products.Specifications;
using Mapster;

namespace ECommerce.UseCases.Queries;

public class GetProductByIdQuery(IReadRepository<Product> productRepository)
{
    public async Task<Result<GetByIdProductResponse>> ExecuteAsync(
        Guid id, 
        CancellationToken cancellationToken = default)
    {
        var spec = new ProductByIdWithBrandAndTypeSpecification(id);
        var product = await productRepository.GetEntityWithSpecAsync(spec, cancellationToken);

        if (product is null)
        {
            return ProductErrors.NotFound(id);
        }

        return product.Adapt<GetByIdProductResponse>();
    }
}
