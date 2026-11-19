using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Common.Caching;
using MediatR;

namespace ECommerce.UseCases.Products.Commands.CreateProduct;

public record CreateProductCommand(
    string Name,
    string Description,
    string PictureUrl,
    decimal Price,
    Guid ProductBrandId,
    Guid ProductTypeId) : IRequest<Result<Guid>>;

public class CreateProductCommandHandler(
    IUnitOfWork unitOfWork,
    ICacheService cacheService) 
    : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateProductCommand request, 
        CancellationToken cancellationToken)
    {
        var productResult = Product.Create(
            request.Name,
            request.Description,
            request.PictureUrl,
            request.Price,
            request.ProductBrandId,
            request.ProductTypeId);

        if (productResult.IsFailure)
        {
            return productResult.Error!;
        }

        unitOfWork.Repository<Product>().Add(productResult.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cacheService.RemoveByPrefixAsync("products", cancellationToken);

        return productResult.Value.Id;
    }
}
