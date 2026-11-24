using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Repositories;
using ECommerce.Domain.Specifications;
using ECommerce.UseCases.Common.Orders;
using ECommerce.UseCases.Common.Payments;
using ECommerce.UseCases.Orders.Commands.CreateOrder;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ECommerce.UseCases.Orders.Commands.ProcessStripeWebhook;

public sealed record ProcessStripeWebhookCommand(string Json, string SignatureHeader) : IRequest<Result>;

public sealed class ProcessStripeWebhookCommandHandler(
    IUnitOfWork uow,
    IPaymentService paymentService,
    IStockAtomicService stockAtomicService,
    IOrderLockingService lockingService,
    IBasketStore basketStore,
    TimeProvider timeProvider,
    ILogger<ProcessStripeWebhookCommandHandler> logger)
    : IRequestHandler<ProcessStripeWebhookCommand, Result>
{
    public async Task<Result> Handle(ProcessStripeWebhookCommand request, CancellationToken ct)
    {
        var verifyResult = paymentService.VerifyWebhook(request.Json, request.SignatureHeader);
        if (verifyResult.IsFailure)
            return verifyResult.Error!;

        var payload = verifyResult.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var existingEvent = await uow.AuditedReadRepository<StripeWebhookEvent>()
            .GetEntityWithSpecAsync(new StripeWebhookEventByStripeIdSpec(payload.EventId), ct);

        if (existingEvent is not null && existingEvent.ProcessingStatus == "Processed")
            return Result.Success();

        if (existingEvent is null)
        {
            var webhookEvent = new StripeWebhookEvent(payload.EventId, payload.EventType, now);
            uow.AuditedRepository<StripeWebhookEvent>().Add(webhookEvent);
            try
            {
                await uow.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex.GetType().Name.Contains("DbUpdate", StringComparison.Ordinal))
            {
                // Duplicate event insert raced.
                return Result.Success();
            }
        }

        try
        {
            var processResult = await ProcessEventAsync(payload, ct);
            if (processResult.IsFailure)
            {
                await MarkEventFailedAsync(payload.EventId, processResult.Error!.Message, ct);
                return processResult;
            }

            await MarkEventProcessedAsync(payload.EventId, ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing Stripe webhook event {EventId}", payload.EventId);
            await MarkEventFailedAsync(payload.EventId, "Processing failed.", ct);
            return Result.Failure(Error.Failure("Webhook.ProcessingFailed", "Webhook processing failed."));
        }
    }

    private async Task MarkEventProcessedAsync(string eventId, CancellationToken ct)
    {
        var ev = await uow.AuditedReadRepository<StripeWebhookEvent>()
            .GetEntityWithSpecAsync(new StripeWebhookEventByStripeIdSpec(eventId), ct);
        if (ev is null) return;
        ev.MarkProcessed(timeProvider.GetUtcNow().UtcDateTime);
        uow.AuditedRepository<StripeWebhookEvent>().Update(ev);
        await uow.SaveChangesAsync(ct);
    }

    private async Task MarkEventFailedAsync(string eventId, string error, CancellationToken ct)
    {
        try
        {
            var ev = await uow.AuditedReadRepository<StripeWebhookEvent>()
                .GetEntityWithSpecAsync(new StripeWebhookEventByStripeIdSpec(eventId), ct);
            if (ev is null) return;
            ev.MarkFailed(error, timeProvider.GetUtcNow().UtcDateTime);
            uow.AuditedRepository<StripeWebhookEvent>().Update(ev);
            await uow.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to mark webhook event {EventId} as failed", eventId);
        }
    }

    private async Task<Result> ProcessEventAsync(StripeWebhookPayload payload, CancellationToken ct)
    {
        return payload.EventType switch
        {
            "payment_intent.succeeded" => await HandlePaymentSucceededAsync(payload, ct),
            "payment_intent.payment_failed" => await HandlePaymentFailedAsync(payload, ct),
            "payment_intent.canceled" => await HandlePaymentCanceledAsync(payload, ct),
            "payment_intent.processing" => await HandlePaymentProcessingAsync(payload, ct),
            "charge.refunded" => await HandleRefundConfirmedAsync(payload, ct),
            "refund.updated" => await HandleRefundUpdatedAsync(payload, ct),
            _ => Result.Success()
        };
    }

    private async Task<Result> HandlePaymentSucceededAsync(StripeWebhookPayload payload, CancellationToken ct)
    {
        var order = await FindOrderAsync(payload, ct);
        if (order is null)
        {
            logger.LogWarning("Webhook {EventId} for unknown order/payment intent {Pi}", payload.EventId, payload.PaymentIntentId);
            return Result.Success();
        }

        if (order.PaymentStatus == PaymentStatus.Refunded)
            return Result.Success();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var txResult = await uow.ExecuteInTransactionResultAsync(async innerCt =>
        {
            await lockingService.LockOrderAndReservationsAsync(order.Id, innerCt);
            var locked = await uow.AuditedReadRepository<Order>()
                .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(order.Id), innerCt);
            if (locked is null)
                return Result.Failure<string>(OrderErrors.NotFound(order.Id));

            if (locked.PaymentStatus == PaymentStatus.Refunded)
                return Result.Success("noop");

            var hasReserved = locked.StockReservations.Any(r => r.Status == ReservationStatus.Reserved);
            var hasFinalized = locked.StockReservations.Any(r => r.Status == ReservationStatus.Finalized);
            var allReleasedOrExpired = locked.StockReservations.Count > 0 &&
                                       locked.StockReservations.All(r => r.Status is ReservationStatus.Released or ReservationStatus.Expired);

            if (hasReserved)
            {
                foreach (var reservation in locked.StockReservations.Where(r => r.Status == ReservationStatus.Reserved))
                    reservation.Finalize(now);

                var paidResult = locked.MarkPaid(now);
                if (paidResult.IsFailure && locked.PaymentStatus != PaymentStatus.Succeeded)
                    return Result.Failure<string>(paidResult.Error!);

                uow.AuditedRepository<Order>().Update(locked);
                await uow.SaveChangesAsync(innerCt);
                return Result.Success("finalized");
            }

            if (hasFinalized)
            {
                if (locked.PaymentStatus != PaymentStatus.Succeeded)
                {
                    locked.MarkPaid(now);
                    uow.AuditedRepository<Order>().Update(locked);
                    await uow.SaveChangesAsync(innerCt);
                }
                return Result.Success("already-finalized");
            }

            if (allReleasedOrExpired)
            {
                var anomaly = locked.MarkSucceededWithReconciliation("PaymentSucceededAfterReservationReleased", now);
                if (anomaly.IsFailure)
                    return Result.Failure<string>(anomaly.Error!);

                uow.AuditedRepository<Order>().Update(locked);
                await uow.SaveChangesAsync(innerCt);
                return Result.Success("reconciliation");
            }

            // Fallback: no reservations, just mark paid if possible
            if (locked.PaymentStatus != PaymentStatus.Succeeded)
            {
                var paidResult = locked.MarkPaid(now);
                if (paidResult.IsFailure) return Result.Failure<string>(paidResult.Error!);
                uow.AuditedRepository<Order>().Update(locked);
                await uow.SaveChangesAsync(innerCt);
            }
            return Result.Success("ok");
        }, ct);

        if (txResult.IsFailure)
            return Result.Failure(txResult.Error!);

        if (txResult.Value is "finalized" or "already-finalized" or "ok")
        {
            var refreshed = await uow.AuditedReadRepository<Order>()
                .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(order.Id), ct);
            if (refreshed is not null && refreshed.PaymentStatus == PaymentStatus.Succeeded && !refreshed.RequiresReconciliation)
            {
                await TryConsumeBasketAsync(refreshed.BuyerId, ct);
            }
        }

        return Result.Success();
    }

    private async Task<Result> HandlePaymentFailedAsync(StripeWebhookPayload payload, CancellationToken ct)
    {
        var order = await FindOrderAsync(payload, ct);
        if (order is null) return Result.Success();
        if (order.PaymentStatus is PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.Refunded)
            return Result.Success();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var txResult = await uow.ExecuteInTransactionResultAsync(async innerCt =>
        {
            await lockingService.LockOrderAndReservationsAsync(order.Id, innerCt);
            var locked = await uow.AuditedReadRepository<Order>()
                .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(order.Id), innerCt);
            if (locked is null) return Result.Failure<string>(OrderErrors.NotFound(order.Id));

            if (locked.PaymentStatus is PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.Refunded)
                return Result.Success("noop");

            var result = locked.MarkPaymentFailed(now);
            if (result.IsFailure && locked.PaymentStatus != PaymentStatus.Failed)
                return Result.Failure<string>(result.Error!);

            foreach (var reservation in locked.StockReservations.Where(r => r.Status == ReservationStatus.Reserved))
            {
                reservation.Release(now);
                await stockAtomicService.IncrementAsync(reservation.ProductId, reservation.Quantity, innerCt);
            }

            uow.AuditedRepository<Order>().Update(locked);
            await uow.SaveChangesAsync(innerCt);
            return Result.Success("ok");
        }, ct);

        return txResult.IsFailure ? Result.Failure(txResult.Error!) : Result.Success();
    }

    private async Task<Result> HandlePaymentCanceledAsync(StripeWebhookPayload payload, CancellationToken ct)
    {
        var order = await FindOrderAsync(payload, ct);
        if (order is null) return Result.Success();
        if (order.PaymentStatus is PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.Refunded)
            return Result.Success();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var txResult = await uow.ExecuteInTransactionResultAsync(async innerCt =>
        {
            await lockingService.LockOrderAndReservationsAsync(order.Id, innerCt);
            var locked = await uow.AuditedReadRepository<Order>()
                .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(order.Id), innerCt);
            if (locked is null) return Result.Failure<string>(OrderErrors.NotFound(order.Id));

            if (locked.PaymentStatus is PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.Refunded)
                return Result.Success("noop");

            var result = locked.MarkPaymentCancelled(now);
            if (result.IsFailure && locked.PaymentStatus != PaymentStatus.Cancelled)
                return Result.Failure<string>(result.Error!);

            foreach (var reservation in locked.StockReservations.Where(r => r.Status == ReservationStatus.Reserved))
            {
                reservation.Release(now);
                await stockAtomicService.IncrementAsync(reservation.ProductId, reservation.Quantity, innerCt);
            }

            uow.AuditedRepository<Order>().Update(locked);
            await uow.SaveChangesAsync(innerCt);
            return Result.Success("ok");
        }, ct);

        return txResult.IsFailure ? Result.Failure(txResult.Error!) : Result.Success();
    }

    private async Task<Result> HandlePaymentProcessingAsync(StripeWebhookPayload payload, CancellationToken ct)
    {
        var order = await FindOrderAsync(payload, ct);
        if (order is null) return Result.Success();
        if (order.PaymentStatus is PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Refunded)
            return Result.Success();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        order.MarkPaymentProcessing(now);
        uow.AuditedRepository<Order>().Update(order);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> HandleRefundConfirmedAsync(StripeWebhookPayload payload, CancellationToken ct)
    {
        var order = await FindOrderAsync(payload, ct);
        if (order is null) return Result.Success();

        var status = payload.RefundStatus;
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase))
            return Result.Success();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var txResult = await uow.ExecuteInTransactionResultAsync(async innerCt =>
        {
            await lockingService.LockOrderAndReservationsAsync(order.Id, innerCt);
            var locked = await uow.AuditedReadRepository<Order>()
                .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(order.Id), innerCt);
            if (locked is null) return Result.Failure<string>(OrderErrors.NotFound(order.Id));

            if (locked.PaymentStatus == PaymentStatus.Refunded && locked.StockReservations.All(r => r.Status != ReservationStatus.Finalized || r.RestockedAtUtc != null))
                return Result.Success("noop");

            var refundResult = locked.MarkRefunded(payload.RefundId, now);
            if (refundResult.IsFailure)
                return Result.Failure<string>(refundResult.Error!);

            foreach (var reservation in locked.StockReservations.Where(r => r.Status == ReservationStatus.Finalized && r.RestockedAtUtc is null))
            {
                await stockAtomicService.IncrementAsync(reservation.ProductId, reservation.Quantity, innerCt);
                reservation.MarkRestocked(now);
            }

            uow.AuditedRepository<Order>().Update(locked);
            await uow.SaveChangesAsync(innerCt);
            return Result.Success("ok");
        }, ct);

        return txResult.IsFailure ? Result.Failure(txResult.Error!) : Result.Success();
    }

    private async Task<Result> HandleRefundUpdatedAsync(StripeWebhookPayload payload, CancellationToken ct)
    {
        if (string.Equals(payload.RefundStatus, "succeeded", StringComparison.OrdinalIgnoreCase))
            return await HandleRefundConfirmedAsync(payload, ct);

        return Result.Success();
    }

    private async Task<Order?> FindOrderAsync(StripeWebhookPayload payload, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(payload.PaymentIntentId))
        {
            var byPi = await uow.AuditedReadRepository<Order>()
                .GetEntityWithSpecAsync(new OrderByPaymentIntentIdSpec(payload.PaymentIntentId), ct);
            if (byPi is not null) return byPi;
        }

        if (!string.IsNullOrWhiteSpace(payload.OrderId) && Guid.TryParse(payload.OrderId, out var orderId))
        {
            return await uow.AuditedReadRepository<Order>()
                .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(orderId), ct);
        }

        return null;
    }

    private async Task TryConsumeBasketAsync(Guid buyerId, CancellationToken ct)
    {
        try
        {
            var key = BasketKey.ForUser(buyerId);
            var basket = await basketStore.GetAsync(key, ct);
            if (basket is null) return;
            // Idempotent: delete if exists. DeleteAsync is revision-aware; load then delete.
            await basketStore.DeleteAsync(key, basket.Revision, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to consume basket for buyer {BuyerId} after payment confirmation", buyerId);
        }
    }
}

public sealed class StripeWebhookEventByStripeIdSpec : BaseSpecification<StripeWebhookEvent>
{
    public StripeWebhookEventByStripeIdSpec(string stripeEventId)
    {
        AddCriteria(x => x.StripeEventId == stripeEventId);
        EnableTracking();
    }
}

public sealed class OrderByPaymentIntentIdSpec : BaseSpecification<Order>
{
    public OrderByPaymentIntentIdSpec(string paymentIntentId)
    {
        AddCriteria(x => x.StripePaymentIntentId == paymentIntentId);
        AddInclude(x => (object)x.Items);
        AddInclude(x => (object)x.StockReservations);
        EnableTracking();
    }
}
