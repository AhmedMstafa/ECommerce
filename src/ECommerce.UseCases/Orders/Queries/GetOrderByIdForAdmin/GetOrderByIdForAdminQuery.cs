using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Orders.Commands.CreateOrder;
using ECommerce.UseCases.Orders.Dtos;
using MediatR;

namespace ECommerce.UseCases.Orders.Queries.GetOrderByIdForAdmin;

public sealed record GetOrderByIdForAdminQuery(Guid OrderId) : IRequest<Result<OrderResponse>>;

public sealed class GetOrderByIdForAdminQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetOrderByIdForAdminQuery, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(GetOrderByIdForAdminQuery request, CancellationToken ct)
    {
        var order = await uow.AuditedReadRepository<Order>()
            .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(request.OrderId), ct);
        if (order is null)
            return Result.Failure<OrderResponse>(OrderErrors.NotFound(request.OrderId));

        return Result.Success(CreateOrderCommandHandler.MapOrder(order));
    }
}
