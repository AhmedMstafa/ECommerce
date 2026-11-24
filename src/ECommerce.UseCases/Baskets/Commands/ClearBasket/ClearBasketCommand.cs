using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.UseCases.Baskets.Dtos;
using ECommerce.UseCases.Common.Baskets;
using MediatR;

namespace ECommerce.UseCases.Baskets.Commands.ClearBasket;

public record ClearBasketCommand(BasketKey Key) : IRequest<Result<BasketResponse>>;

public class ClearBasketCommandHandler(IBasketService basketService)
    : IRequestHandler<ClearBasketCommand, Result<BasketResponse>>
{
    public Task<Result<BasketResponse>> Handle(
        ClearBasketCommand request,
        CancellationToken cancellationToken) =>
        basketService.ClearAsync(request.Key, cancellationToken);
}
