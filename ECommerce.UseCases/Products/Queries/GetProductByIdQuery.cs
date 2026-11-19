using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Products.Dtos;
using ECommerce.UseCases.Products.Specifications;
using Mapster;
using MediatR;

namespace ECommerce.UseCases.Products.Queries;

public record GetProductByIdQuery(Guid Id) : IRequest<Result<GetByIdProductResponse>>;

public class GetProductByIdQueryHandler(IReadRepository<Product> productRepository) 
    : IRequestHandler<GetProductByIdQuery, Result<GetByIdProductResponse>>
{
    public async Task<Result<GetByIdProductResponse>> Handle(
        GetProductByIdQuery request, 
        CancellationToken cancellationToken)
    {
        var spec = new ProductByIdWithBrandAndTypeSpecification(request.Id);
        var product = await productRepository.GetEntityWithSpecAsync(spec, cancellationToken);

        if (product is null)
        {
            return ProductErrors.NotFound(request.Id);
        }

        return product.Adapt<GetByIdProductResponse>();
    }
}
