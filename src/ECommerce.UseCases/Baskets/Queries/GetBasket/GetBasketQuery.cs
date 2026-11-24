using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.UseCases.Baskets.Dtos;
using ECommerce.UseCases.Common.Baskets;
using MediatR;

namespace ECommerce.UseCases.Baskets.Queries.GetBasket;

public record GetBasketQuery(BasketKey Key) : IRequest<Result<BasketResponse>>;

public class GetBasketQueryHandler(IBasketService basketService)
    : IRequestHandler<GetBasketQuery, Result<BasketResponse>>
{
    public Task<Result<BasketResponse>> Handle(
        GetBasketQuery request,
        CancellationToken cancellationToken) =>
        basketService.GetAsync(request.Key, cancellationToken);
}
