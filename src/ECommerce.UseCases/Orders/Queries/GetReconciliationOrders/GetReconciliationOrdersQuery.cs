using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.Domain.Specifications;
using ECommerce.Domain.Specifications.Builders;
using ECommerce.UseCases.Orders.Commands.CreateOrder;
using ECommerce.UseCases.Orders.Dtos;
using MediatR;

namespace ECommerce.UseCases.Orders.Queries.GetReconciliationOrders;

public sealed record GetReconciliationOrdersQuery : IRequest<Result<IReadOnlyList<OrderResponse>>>;

public sealed class ReconciliationOrdersSpec : BaseSpecification<Order>
{
    public ReconciliationOrdersSpec()
    {
        AddCriteria(x => x.RequiresReconciliation);
        Query.Include(x => x.Items);
        Query.Include(x => x.StockReservations);
        AddOrderByDescending(x => x.CreatedAtUtc);
    }
}

public sealed class GetReconciliationOrdersQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetReconciliationOrdersQuery, Result<IReadOnlyList<OrderResponse>>>
{
    public async Task<Result<IReadOnlyList<OrderResponse>>> Handle(GetReconciliationOrdersQuery request, CancellationToken ct)
    {
        var orders = await uow.AuditedReadRepository<Order>()
            .GetAllWithSpecAsync(new ReconciliationOrdersSpec(), ct);
        return Result.Success<IReadOnlyList<OrderResponse>>(orders.Select(o => CreateOrderCommandHandler.MapOrder(o)).ToList());
    }
}
