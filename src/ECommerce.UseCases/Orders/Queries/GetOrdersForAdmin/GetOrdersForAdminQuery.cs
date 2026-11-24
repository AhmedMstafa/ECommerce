using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Repositories;
using ECommerce.Domain.Specifications;
using ECommerce.UseCases.Orders.Commands.CreateOrder;
using ECommerce.UseCases.Orders.Dtos;
using MediatR;

namespace ECommerce.UseCases.Orders.Queries.GetOrdersForAdmin;

public sealed record GetOrdersForAdminQuery(
    OrderStatus? Status,
    DateTime? FromUtc,
    DateTime? ToUtc) : IRequest<Result<IReadOnlyList<OrderResponse>>>;

public sealed class AdminOrdersSpec : BaseSpecification<Order>
{
    public AdminOrdersSpec(OrderStatus? status, DateTime? fromUtc, DateTime? toUtc)
    {
        if (status.HasValue)
            AddCriteria(x => x.OrderStatus == status.Value);
        if (fromUtc.HasValue)
            AddWhere(x => x.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            AddWhere(x => x.CreatedAtUtc <= toUtc.Value);
        AddInclude(x => (object)x.Items);
        AddInclude(x => (object)x.StockReservations);
        AddOrderByDescending(x => x.CreatedAtUtc);
    }
}

public sealed class GetOrdersForAdminQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetOrdersForAdminQuery, Result<IReadOnlyList<OrderResponse>>>
{
    public async Task<Result<IReadOnlyList<OrderResponse>>> Handle(GetOrdersForAdminQuery request, CancellationToken ct)
    {
        var orders = await uow.AuditedReadRepository<Order>()
            .GetAllWithSpecAsync(new AdminOrdersSpec(request.Status, request.FromUtc, request.ToUtc), ct);
        return Result.Success<IReadOnlyList<OrderResponse>>(orders.Select(o => CreateOrderCommandHandler.MapOrder(o)).ToList());
    }
}
