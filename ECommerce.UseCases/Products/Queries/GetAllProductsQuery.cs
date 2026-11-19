using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Products.Dtos;
using ECommerce.UseCases.Products.Specifications;
using Mapster;
using MediatR;

namespace ECommerce.UseCases.Products.Queries;

public record GetAllProductsQuery(ProductSpecificationParameters? Parameters = null) 
    : IRequest<IReadOnlyList<GetAllProductsResponse>>;

public class GetAllProductsQueryHandler(IReadRepository<Product> productRepository) 
    : IRequestHandler<GetAllProductsQuery, IReadOnlyList<GetAllProductsResponse>>
{
    public async Task<IReadOnlyList<GetAllProductsResponse>> Handle(
        GetAllProductsQuery request, 
        CancellationToken cancellationToken)
    {
        var spec = request.Parameters is not null
            ? new ProductsWithBrandAndTypeSpecification(request.Parameters)
            : new ProductsWithBrandAndTypeSpecification();

        var products = await productRepository.GetAllWithSpecAsync(spec, cancellationToken);
        return products.Adapt<IReadOnlyList<GetAllProductsResponse>>();
    }
}
