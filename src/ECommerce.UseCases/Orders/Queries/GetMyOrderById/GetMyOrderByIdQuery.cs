using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Orders.Commands.CreateOrder;
using ECommerce.UseCases.Orders.Dtos;
using MediatR;

namespace ECommerce.UseCases.Orders.Queries.GetMyOrderById;

public sealed record GetMyOrderByIdQuery(Guid BuyerId, Guid OrderId) : IRequest<Result<OrderResponse>>;

public sealed class GetMyOrderByIdQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetMyOrderByIdQuery, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(GetMyOrderByIdQuery request, CancellationToken ct)
    {
        var order = await uow.AuditedReadRepository<Order>()
            .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(request.OrderId), ct);
        if (order is null)
            return Result.Failure<OrderResponse>(OrderErrors.NotFound(request.OrderId));
        if (order.BuyerId != request.BuyerId)
            return Result.Failure<OrderResponse>(OrderErrors.NotOwner);

        return Result.Success(CreateOrderCommandHandler.MapOrder(order));
    }
}
