using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Common.Orders;
using ECommerce.UseCases.Common.Payments;
using ECommerce.UseCases.Orders.Commands.CreateOrder;
using ECommerce.UseCases.Orders.Dtos;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Orders.Commands.CancelOrder;

public sealed record CancelOrderCommand(Guid BuyerId, Guid OrderId, bool IsAdmin = false) : IRequest<Result<OrderResponse>>;

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}

public sealed class CancelOrderCommandHandler(
    IUnitOfWork uow,
    IPaymentService paymentService,
    IStockAtomicService stockAtomicService,
    IOrderLockingService lockingService,
    TimeProvider timeProvider)
    : IRequestHandler<CancelOrderCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(CancelOrderCommand request, CancellationToken ct)
    {
        var order = await uow.AuditedReadRepository<Order>()
            .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(request.OrderId), ct);
        if (order is null)
            return Result.Failure<OrderResponse>(OrderErrors.NotFound(request.OrderId));

        if (!request.IsAdmin && order.BuyerId != request.BuyerId)
            return Result.Failure<OrderResponse>(OrderErrors.NotOwner);

        if (order.OrderStatus is OrderStatus.Shipped or OrderStatus.Delivered)
            return Result.Failure<OrderResponse>(OrderErrors.CannotCancel);

        // Paid order: customer requests refund (admin initiates actual Stripe refund)
        if (order.IsPaid())
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var refundRequestResult = order.RequestRefund(now);
            if (refundRequestResult.IsFailure)
                return Result.Failure<OrderResponse>(refundRequestResult.Error!);

            uow.AuditedRepository<Order>().Update(order);
            await uow.SaveChangesAsync(ct);
            return Result.Success(CreateOrderCommandHandler.MapOrder(order));
        }

        // Unpaid order cancellation
        if (order.OrderStatus == OrderStatus.Cancelled)
            return Result.Success(CreateOrderCommandHandler.MapOrder(order));

        // Cancel PaymentIntent outside DB transaction first, if exists.
        bool stripeCancelIndicatesSucceeded = false;
        if (!string.IsNullOrWhiteSpace(order.StripePaymentIntentId))
        {
            var cancelResult = await paymentService.CancelPaymentIntentAsync(
                order.StripePaymentIntentId,
                $"cancel_pi_{order.Id}",
                ct);

            if (cancelResult.IsFailure)
            {
                if (cancelResult.Error!.Code == "Payment.AlreadySucceeded")
                {
                    stripeCancelIndicatesSucceeded = true;
                }
                else if (cancelResult.Error.Type == ErrorType.Conflict)
                {
                    // Can't cancel - could be in processing/requires_action uncancelable state. Do not release stock.
                    return Result.Failure<OrderResponse>(cancelResult.Error);
                }
                else
                {
                    // Transient failure - do not change DB, do not release stock.
                    return Result.Failure<OrderResponse>(cancelResult.Error);
                }
            }
        }

        if (stripeCancelIndicatesSucceeded)
        {
            // Do not release stock. Payment succeeded concurrently.
            return Result.Failure<OrderResponse>(OrderErrors.PaymentAlreadySucceeded);
        }

        // DB transaction: lock, recheck, cancel+release only if still unpaid/reserved.
        try
        {
            var cancelTxResult = await uow.ExecuteInTransactionResultAsync(async innerCt =>
            {
                await lockingService.LockOrderAndReservationsAsync(request.OrderId, innerCt);

                var lockedOrder = await uow.AuditedReadRepository<Order>()
                    .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(request.OrderId), innerCt);
                if (lockedOrder is null)
                    return Result.Failure<Order>(OrderErrors.NotFound(request.OrderId));

                var now = timeProvider.GetUtcNow().UtcDateTime;

                // Recheck - if payment succeeded/finalized, do not release stock.
                if (lockedOrder.PaymentStatus is PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.Refunded)
                    return Result.Failure<Order>(OrderErrors.PaymentAlreadySucceeded);

                if (lockedOrder.StockReservations.Any(r => r.Status == ReservationStatus.Finalized))
                    return Result.Failure<Order>(OrderErrors.PaymentAlreadySucceeded);

                if (lockedOrder.OrderStatus == OrderStatus.Cancelled)
                    return Result.Success(lockedOrder);

                if (lockedOrder.OrderStatus is OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Processing or OrderStatus.Paid)
                    return Result.Failure<Order>(OrderErrors.CannotCancel);

                var cancelResult = lockedOrder.CancelUnpaid(now);
                if (cancelResult.IsFailure)
                    return Result.Failure<Order>(cancelResult.Error!);

                foreach (var reservation in lockedOrder.StockReservations.Where(r => r.Status == ReservationStatus.Reserved))
                {
                    reservation.Release(now);
                    await stockAtomicService.IncrementAsync(reservation.ProductId, reservation.Quantity, innerCt);
                }

                uow.AuditedRepository<Order>().Update(lockedOrder);
                await uow.SaveChangesAsync(innerCt);
                return Result.Success(lockedOrder);
            }, ct);

            if (cancelTxResult.IsFailure)
                return Result.Failure<OrderResponse>(cancelTxResult.Error!);

            return Result.Success(CreateOrderCommandHandler.MapOrder(cancelTxResult.Value));
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("DbUpdateConcurrency", StringComparison.Ordinal))
        {
            return Result.Failure<OrderResponse>(OrderErrors.ConcurrentUpdate);
        }
    }
}
