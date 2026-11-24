using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.UseCases.Baskets.Dtos;
using ECommerce.UseCases.Common.Baskets;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;

namespace ECommerce.UseCases.Baskets.Commands.UpdateBasketItemQuantity;

public record UpdateBasketItemQuantityCommand(BasketKey Key, Guid ProductId, int Quantity)
    : IRequest<Result<BasketResponse>>;

public class UpdateBasketItemQuantityCommandValidator : AbstractValidator<UpdateBasketItemQuantityCommand>
{
    public UpdateBasketItemQuantityCommandValidator(IOptions<BasketSettings> options)
    {
        var settings = options.Value;

        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than zero.")
            .LessThanOrEqualTo(settings.MaximumQuantityPerProduct)
            .WithMessage($"Quantity must not exceed {settings.MaximumQuantityPerProduct}.");
    }
}

public class UpdateBasketItemQuantityCommandHandler(IBasketService basketService)
    : IRequestHandler<UpdateBasketItemQuantityCommand, Result<BasketResponse>>
{
    public Task<Result<BasketResponse>> Handle(
        UpdateBasketItemQuantityCommand request,
        CancellationToken cancellationToken) =>
        basketService.UpdateQuantityAsync(request.Key, request.ProductId, request.Quantity, cancellationToken);
}
