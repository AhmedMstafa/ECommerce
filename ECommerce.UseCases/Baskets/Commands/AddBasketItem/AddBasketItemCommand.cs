using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.UseCases.Baskets.Dtos;
using ECommerce.UseCases.Common.Baskets;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;

namespace ECommerce.UseCases.Baskets.Commands.AddBasketItem;

public record AddBasketItemCommand(BasketKey Key, Guid ProductId, int Quantity) : IRequest<Result<BasketResponse>>;

public class AddBasketItemCommandValidator : AbstractValidator<AddBasketItemCommand>
{
    public AddBasketItemCommandValidator(IOptions<BasketSettings> options)
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

public class AddBasketItemCommandHandler(IBasketService basketService)
    : IRequestHandler<AddBasketItemCommand, Result<BasketResponse>>
{
    public Task<Result<BasketResponse>> Handle(
        AddBasketItemCommand request,
        CancellationToken cancellationToken) =>
        basketService.AddItemAsync(request.Key, request.ProductId, request.Quantity, cancellationToken);
}
