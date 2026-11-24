using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Orders.Commands.CreateOrder;
using ECommerce.UseCases.Orders.Dtos;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Orders.Commands.UpdateOrderFulfillment;

public sealed record UpdateOrderFulfillmentCommand(Guid OrderId, OrderStatus NewStatus) : IRequest<Result<OrderResponse>>;

public sealed class UpdateOrderFulfillmentCommandValidator : AbstractValidator<UpdateOrderFulfillmentCommand>
{
    public UpdateOrderFulfillmentCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.NewStatus).IsInEnum();
    }
}

public sealed class UpdateOrderFulfillmentCommandHandler(IUnitOfWork uow, TimeProvider timeProvider)
    : IRequestHandler<UpdateOrderFulfillmentCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(UpdateOrderFulfillmentCommand request, CancellationToken ct)
    {
        var order = await uow.AuditedReadRepository<Order>()
            .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(request.OrderId), ct);
        if (order is null)
            return Result.Failure<OrderResponse>(OrderErrors.NotFound(request.OrderId));

        var result = order.AdvanceFulfillment(request.NewStatus, timeProvider.GetUtcNow().UtcDateTime);
        if (result.IsFailure)
            return Result.Failure<OrderResponse>(result.Error!);

        uow.AuditedRepository<Order>().Update(order);
        await uow.SaveChangesAsync(ct);

        return Result.Success(CreateOrderCommandHandler.MapOrder(order));
    }
}
