using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.UseCases.Baskets.Dtos;
using ECommerce.UseCases.Common.Baskets;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Baskets.Commands.RemoveBasketItem;

public record RemoveBasketItemCommand(BasketKey Key, Guid ProductId) : IRequest<Result<BasketResponse>>;

public class RemoveBasketItemCommandValidator : AbstractValidator<RemoveBasketItemCommand>
{
    public RemoveBasketItemCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required.");
    }
}

public class RemoveBasketItemCommandHandler(IBasketService basketService)
    : IRequestHandler<RemoveBasketItemCommand, Result<BasketResponse>>
{
    public Task<Result<BasketResponse>> Handle(
        RemoveBasketItemCommand request,
        CancellationToken cancellationToken) =>
        basketService.RemoveItemAsync(request.Key, request.ProductId, cancellationToken);
}
