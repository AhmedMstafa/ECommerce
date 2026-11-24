using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.Domain.Specifications;
using ECommerce.UseCases.Orders.Commands.CreateOrder;
using ECommerce.UseCases.Orders.Dtos;
using MediatR;

namespace ECommerce.UseCases.Orders.Queries.GetMyOrders;

public sealed record GetMyOrdersQuery(Guid BuyerId) : IRequest<Result<IReadOnlyList<OrderResponse>>>;

public sealed class OrdersByBuyerSpec : BaseSpecification<Order>
{
    public OrdersByBuyerSpec(Guid buyerId)
    {
        AddCriteria(x => x.BuyerId == buyerId);
        AddInclude(x => (object)x.Items);
        AddInclude(x => (object)x.StockReservations);
        AddOrderByDescending(x => x.CreatedAtUtc);
    }
}

public sealed class GetMyOrdersQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetMyOrdersQuery, Result<IReadOnlyList<OrderResponse>>>
{
    public async Task<Result<IReadOnlyList<OrderResponse>>> Handle(GetMyOrdersQuery request, CancellationToken ct)
    {
        var orders = await uow.AuditedReadRepository<Order>()
            .GetAllWithSpecAsync(new OrdersByBuyerSpec(request.BuyerId), ct);
        return Result.Success<IReadOnlyList<OrderResponse>>(orders.Select(o => CreateOrderCommandHandler.MapOrder(o)).ToList());
    }
}
