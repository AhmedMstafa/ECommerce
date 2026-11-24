using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Repositories;
using ECommerce.Domain.Specifications;
using ECommerce.Domain.Specifications.Builders;
using ECommerce.UseCases.Common.Orders;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.BackgroundJobs;

public sealed class ReservationExpirationJob(
    IUnitOfWork uow,
    IOrderLockingService lockingService,
    IStockAtomicService stockAtomicService,
    TimeProvider timeProvider,
    ILogger<ReservationExpirationJob> logger)
{
    [Queue("default")]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 10, 30, 120 })]
    [DisableConcurrentExecution(300)]
    public async Task ExpireReservationsAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expired = await uow.AuditedReadRepository<StockReservation>()
            .GetAllWithSpecAsync(new ExpiredReservedReservationsSpec(now), cancellationToken);

        if (expired.Count == 0)
            return;

        var orderIds = expired.Select(r => r.OrderId).Distinct().ToList();
        logger.LogInformation("Found {Count} expired stock reservations across {OrderCount} orders", expired.Count, orderIds.Count);

        foreach (var orderId in orderIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await uow.ExecuteInTransactionAsync(async innerCt =>
                {
                    await lockingService.LockOrderAndReservationsAsync(orderId, innerCt);

                    var order = await uow.AuditedReadRepository<Order>()
                        .GetEntityWithSpecAsync(new OrderWithReservationsSpec(orderId), innerCt);
                    if (order is null) return;

                    var txNow = timeProvider.GetUtcNow().UtcDateTime;

                    // Never release stock if payment succeeded/finalized.
                    if (order.PaymentStatus is PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.Refunded)
                        return;

                    if (order.StockReservations.Any(r => r.Status == ReservationStatus.Finalized))
                        return;

                    var toRelease = order.StockReservations
                        .Where(r => r.Status == ReservationStatus.Reserved && r.ExpiresAtUtc <= txNow)
                        .ToList();

                    if (toRelease.Count == 0)
                        return;

                    foreach (var reservation in toRelease)
                    {
                        reservation.Release(txNow, expired: true);
                        await stockAtomicService.IncrementAsync(reservation.ProductId, reservation.Quantity, innerCt);
                    }

                    // If order is still pending/unpaid, mark as cancelled/expired-payment-failed equivalent?
                    // Terminal expiry: cancel order, release reservation. Payment status should reflect expiry/cancel.
                    if (order.OrderStatus == OrderStatus.Pending &&
                        order.PaymentStatus is PaymentStatus.Pending or PaymentStatus.RequiresAction)
                    {
                        order.MarkPaymentCancelled(txNow);
                    }

                    uow.AuditedRepository<Order>().Update(order);
                    await uow.SaveChangesAsync(innerCt);
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to expire reservations for order {OrderId}", orderId);
            }
        }
    }
}

public sealed class ExpiredReservedReservationsSpec : BaseSpecification<StockReservation>
{
    public ExpiredReservedReservationsSpec(DateTime utcNow)
    {
        AddCriteria(r => r.Status == ReservationStatus.Reserved && r.ExpiresAtUtc <= utcNow);
    }
}

public sealed class OrderWithReservationsSpec : BaseSpecification<Order>
{
    public OrderWithReservationsSpec(Guid orderId)
    {
        AddCriteria(o => o.Id == orderId);
        Query.Include(o => o.Items);
        Query.Include(o => o.StockReservations);
        EnableTracking();
    }
}