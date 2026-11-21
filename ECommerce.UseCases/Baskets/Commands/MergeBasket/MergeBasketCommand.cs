using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.UseCases.Baskets.Dtos;
using ECommerce.UseCases.Common.Baskets;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Baskets.Commands.MergeBasket;

public record MergeBasketCommand(BasketKey UserKey, string? GuestToken)
    : IRequest<Result<BasketResponse>>;

public class MergeBasketCommandValidator : AbstractValidator<MergeBasketCommand>
{
    public MergeBasketCommandValidator()
    {
        RuleFor(x => x.UserKey)
            .NotNull()
            .Must(key => key.Kind == BasketKind.User)
            .WithMessage("An authenticated user basket is required.");

        RuleFor(x => x.GuestToken)
            .Must(GuestToken.IsValid)
            .When(x => !string.IsNullOrWhiteSpace(x.GuestToken))
            .WithMessage("The guest token is invalid.");
    }
}

public class MergeBasketCommandHandler(IBasketService basketService)
    : IRequestHandler<MergeBasketCommand, Result<BasketResponse>>
{
    public Task<Result<BasketResponse>> Handle(
        MergeBasketCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.GuestToken))
        {
            return basketService.GetAsync(request.UserKey, cancellationToken);
        }

        return basketService.MergeAsync(request.UserKey, request.GuestToken, cancellationToken);
    }
}
