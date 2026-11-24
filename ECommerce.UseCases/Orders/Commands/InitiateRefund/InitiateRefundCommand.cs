using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Common.Payments;
using ECommerce.UseCases.Orders.Commands.CreateOrder;
using ECommerce.UseCases.Orders.Dtos;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Orders.Commands.InitiateRefund;

public sealed record InitiateRefundCommand(Guid OrderId, Guid AdminUserId) : IRequest<Result<OrderResponse>>;

public sealed class InitiateRefundCommandValidator : AbstractValidator<InitiateRefundCommand>
{
    public InitiateRefundCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}

public sealed class InitiateRefundCommandHandler(
    IUnitOfWork uow,
    IPaymentService paymentService,
    TimeProvider timeProvider)
    : IRequestHandler<InitiateRefundCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(InitiateRefundCommand request, CancellationToken ct)
    {
        var order = await uow.AuditedReadRepository<Order>()
            .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(request.OrderId), ct);
        if (order is null)
            return Result.Failure<OrderResponse>(OrderErrors.NotFound(request.OrderId));

        if (order.PaymentStatus == PaymentStatus.Refunded)
            return Result.Success(CreateOrderCommandHandler.MapOrder(order));

        if (string.IsNullOrWhiteSpace(order.StripePaymentIntentId))
            return Result.Failure<OrderResponse>(OrderErrors.RefundNotAllowed);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var markResult = order.MarkRefundPending(now);
        if (markResult.IsFailure)
            return Result.Failure<OrderResponse>(markResult.Error!);

        uow.AuditedRepository<Order>().Update(order);
        await uow.SaveChangesAsync(ct);

        var refundResult = await paymentService.CreateRefundAsync(
            order.StripePaymentIntentId,
            $"refund_{order.Id}",
            ct);

        if (refundResult.IsFailure)
        {
            var failNow = timeProvider.GetUtcNow().UtcDateTime;
            order.RevertRefundPendingToSucceeded(failNow);
            order.FlagRefundInitiationFailure("RefundInitiationFailed", failNow);
            uow.AuditedRepository<Order>().Update(order);
            await uow.SaveChangesAsync(ct);
            return Result.Failure<OrderResponse>(Error.Failure("Order.RefundInitiationFailed",
                "Refund initiation failed. The payment remains captured; an administrator can retry."));
        }

        order.SetRefundId(refundResult.Value.RefundId, timeProvider.GetUtcNow().UtcDateTime);
        uow.AuditedRepository<Order>().Update(order);
        await uow.SaveChangesAsync(ct);

        // Reload to return fresh state
        var refreshed = await uow.AuditedReadRepository<Order>()
            .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(request.OrderId), ct);
        return Result.Success(CreateOrderCommandHandler.MapOrder(refreshed ?? order));
    }
}
