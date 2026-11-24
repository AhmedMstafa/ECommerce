using ECommerce.Domain.Common;
using ECommerce.Domain.Enums;

namespace ECommerce.Domain.Entities;

public sealed class Order : AuditedEntity
{
    private readonly List<OrderItem> _items = [];
    private readonly List<StockReservation> _stockReservations = [];

    public Guid BuyerId { get; private set; }
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<StockReservation> StockReservations => _stockReservations.AsReadOnly();
    public OrderShippingAddress ShippingAddressSnapshot { get; private set; } = default!;
    public Guid DeliveryMethodId { get; private set; }
    public string DeliveryMethodNameSnapshot { get; private set; } = default!;
    public decimal DeliveryMethodPriceSnapshot { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DeliveryFee { get; private set; }
    public decimal Total { get; private set; }
    public OrderStatus OrderStatus { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public string? StripePaymentIntentId { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public DateTime ReservationExpiresAtUtc { get; private set; }
    public bool RequiresReconciliation { get; private set; }
    public string? ReconciliationReason { get; private set; }
    public DateTime? ReconciliationResolvedAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public DateTime? RefundRequestedAtUtc { get; private set; }
    public string? RefundId { get; private set; }
    public DateTime? RefundedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = default!;

    private Order() { }

    private Order(
        Guid id,
        Guid buyerId,
        OrderShippingAddress shippingAddressSnapshot,
        Guid deliveryMethodId,
        string deliveryMethodNameSnapshot,
        decimal deliveryMethodPriceSnapshot,
        decimal subtotal,
        string idempotencyKey,
        DateTime createdAtUtc,
        DateTime reservationExpiresAtUtc)
    {
        Id = id == default ? Guid.NewGuid() : id;
        BuyerId = buyerId;
        ShippingAddressSnapshot = shippingAddressSnapshot;
        DeliveryMethodId = deliveryMethodId;
        DeliveryMethodNameSnapshot = deliveryMethodNameSnapshot;
        DeliveryMethodPriceSnapshot = deliveryMethodPriceSnapshot;
        Subtotal = subtotal;
        DeliveryFee = deliveryMethodPriceSnapshot;
        Total = subtotal + deliveryMethodPriceSnapshot;
        OrderStatus = OrderStatus.Pending;
        PaymentStatus = PaymentStatus.Pending;
        IdempotencyKey = idempotencyKey;
        ReservationExpiresAtUtc = reservationExpiresAtUtc;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = new DateTimeOffset(createdAtUtc, TimeSpan.Zero);
    }

    public static Result<Order> Create(
        Guid buyerId,
        OrderShippingAddress shippingAddressSnapshot,
        Guid deliveryMethodId,
        string deliveryMethodNameSnapshot,
        decimal deliveryMethodPriceSnapshot,
        decimal subtotal,
        string idempotencyKey,
        DateTime createdAtUtc,
        DateTime reservationExpiresAtUtc,
        Guid id = default)
    {
        if (buyerId == Guid.Empty)
            return Result.Failure<Order>(Error.Validation("Order.InvalidBuyer", "A valid buyer is required."));
        if (shippingAddressSnapshot is null)
            return Result.Failure<Order>(OrderErrors.InvalidAddress);
        if (deliveryMethodId == Guid.Empty)
            return Result.Failure<Order>(OrderErrors.DeliveryMethodNotFound);
        if (string.IsNullOrWhiteSpace(deliveryMethodNameSnapshot))
            return Result.Failure<Order>(Error.Validation("Order.InvalidDeliveryMethod", "Delivery method name is required."));
        if (deliveryMethodPriceSnapshot < 0)
            return Result.Failure<Order>(Error.Validation("Order.InvalidDeliveryPrice", "Delivery price cannot be negative."));
        if (subtotal < 0)
            return Result.Failure<Order>(Error.Validation("Order.InvalidSubtotal", "Subtotal cannot be negative."));
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result.Failure<Order>(OrderErrors.IdempotencyKeyRequired);
        if (idempotencyKey.Length > 128)
            return Result.Failure<Order>(OrderErrors.IdempotencyKeyTooLong);

        return new Order(id, buyerId, shippingAddressSnapshot, deliveryMethodId,
            deliveryMethodNameSnapshot.Trim(), deliveryMethodPriceSnapshot, subtotal,
            idempotencyKey.Trim(), createdAtUtc, reservationExpiresAtUtc);
    }

    public void AddItem(OrderItem item)
    {
        _items.Add(item);
    }

    public void AddReservation(StockReservation reservation)
    {
        _stockReservations.Add(reservation);
    }

    public void SetPaymentIntentId(string paymentIntentId, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId))
            throw new ArgumentException("PaymentIntentId is required.", nameof(paymentIntentId));

        StripePaymentIntentId = paymentIntentId;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    public Result MarkPaymentIntentFailed(DateTime utcNow)
    {
        if (PaymentStatus == PaymentStatus.Succeeded || PaymentStatus == PaymentStatus.Refunded)
            return OrderErrors.InvalidPaymentStatusTransition;

        PaymentStatus = PaymentStatus.Failed;
        OrderStatus = OrderStatus.Cancelled;
        CancelledAtUtc = utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public Result MarkPaid(DateTime utcNow)
    {
        if (PaymentStatus == PaymentStatus.Succeeded)
            return Result.Success(); // idempotent

        if (PaymentStatus is PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Refunded)
            return OrderErrors.InvalidPaymentStatusTransition;

        if (OrderStatus is not OrderStatus.Pending)
            return OrderErrors.InvalidStatusTransition;

        PaymentStatus = PaymentStatus.Succeeded;
        OrderStatus = OrderStatus.Paid;
        PaidAtUtc = utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public Result MarkPaymentRequiresAction(DateTime utcNow)
    {
        if (PaymentStatus == PaymentStatus.Succeeded)
            return OrderErrors.InvalidPaymentStatusTransition;
        if (PaymentStatus is PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Refunded)
            return OrderErrors.InvalidPaymentStatusTransition;

        PaymentStatus = PaymentStatus.RequiresAction;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public Result MarkPaymentProcessing(DateTime utcNow)
    {
        if (PaymentStatus == PaymentStatus.Succeeded)
            return Result.Success();
        if (PaymentStatus is PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Refunded)
            return OrderErrors.InvalidPaymentStatusTransition;

        PaymentStatus = PaymentStatus.Pending;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public Result MarkPaymentFailed(DateTime utcNow)
    {
        if (PaymentStatus == PaymentStatus.Succeeded || PaymentStatus == PaymentStatus.Refunded)
            return OrderErrors.InvalidPaymentStatusTransition;
        if (PaymentStatus == PaymentStatus.Failed && OrderStatus == OrderStatus.Cancelled)
            return Result.Success();

        PaymentStatus = PaymentStatus.Failed;
        OrderStatus = OrderStatus.Cancelled;
        CancelledAtUtc ??= utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public Result MarkPaymentCancelled(DateTime utcNow)
    {
        if (PaymentStatus == PaymentStatus.Succeeded || PaymentStatus == PaymentStatus.Refunded)
            return OrderErrors.InvalidPaymentStatusTransition;
        if (PaymentStatus == PaymentStatus.Cancelled && OrderStatus == OrderStatus.Cancelled)
            return Result.Success();

        PaymentStatus = PaymentStatus.Cancelled;
        OrderStatus = OrderStatus.Cancelled;
        CancelledAtUtc ??= utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public Result MarkReconciliationRequired(string reason, DateTime utcNow)
    {
        if (PaymentStatus != PaymentStatus.Succeeded)
            return OrderErrors.InvalidPaymentStatusTransition;

        RequiresReconciliation = true;
        ReconciliationReason = reason;
        OrderStatus = OrderStatus.Cancelled;
        CancelledAtUtc ??= utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public Result MarkSucceededWithReconciliation(string reason, DateTime utcNow)
    {
        if (PaymentStatus == PaymentStatus.Refunded)
            return OrderErrors.InvalidPaymentStatusTransition;

        if (PaymentStatus == PaymentStatus.Succeeded && RequiresReconciliation && OrderStatus == OrderStatus.Cancelled)
        {
            if (string.IsNullOrWhiteSpace(ReconciliationReason))
                ReconciliationReason = reason;
            return Result.Success();
        }

        if (PaymentStatus == PaymentStatus.Succeeded && !RequiresReconciliation)
            return OrderErrors.InvalidPaymentStatusTransition;

        PaymentStatus = PaymentStatus.Succeeded;
        OrderStatus = OrderStatus.Cancelled;
        RequiresReconciliation = true;
        ReconciliationReason = reason;
        PaidAtUtc ??= utcNow;
        CancelledAtUtc ??= utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public void FlagRefundInitiationFailure(string reason, DateTime utcNow)
    {
        RequiresReconciliation = true;
        ReconciliationReason = reason.Length > 500 ? reason[..500] : reason;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    public void SetRefundId(string refundId, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(refundId)) return;
        RefundId = refundId;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    public Result CancelUnpaid(DateTime utcNow)
    {
        if (PaymentStatus == PaymentStatus.Succeeded || PaymentStatus == PaymentStatus.RefundPending || PaymentStatus == PaymentStatus.Refunded)
            return OrderErrors.PaymentAlreadySucceeded;

        if (OrderStatus == OrderStatus.Cancelled)
            return Result.Success(); // idempotent

        if (OrderStatus is OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Processing or OrderStatus.Paid)
            return OrderErrors.CannotCancel;

        OrderStatus = OrderStatus.Cancelled;
        PaymentStatus = PaymentStatus.Cancelled;
        CancelledAtUtc = utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public Result RequestRefund(DateTime utcNow)
    {
        if (PaymentStatus == PaymentStatus.Refunded)
            return Result.Success();
        if (PaymentStatus == PaymentStatus.RefundPending)
        {
            RefundRequestedAtUtc ??= utcNow;
            UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
            return Result.Success();
        }
        if (PaymentStatus != PaymentStatus.Succeeded)
            return OrderErrors.RefundNotAllowed;
        if (OrderStatus is OrderStatus.Shipped or OrderStatus.Delivered)
            return OrderErrors.RefundNotAllowed;

        RefundRequestedAtUtc = utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public Result MarkRefundPending(DateTime utcNow)
    {
        if (PaymentStatus == PaymentStatus.Refunded)
            return Result.Success();
        if (PaymentStatus != PaymentStatus.Succeeded)
            return OrderErrors.RefundNotAllowed;
        if (OrderStatus is OrderStatus.Shipped or OrderStatus.Delivered)
            return OrderErrors.RefundNotAllowed;

        PaymentStatus = PaymentStatus.RefundPending;
        RefundRequestedAtUtc ??= utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public Result MarkRefunded(string? refundId, DateTime utcNow)
    {
        if (PaymentStatus == PaymentStatus.Refunded)
        {
            if (!string.IsNullOrWhiteSpace(refundId) && string.IsNullOrWhiteSpace(RefundId))
                RefundId = refundId;
            return Result.Success();
        }

        if (PaymentStatus is not (PaymentStatus.RefundPending or PaymentStatus.Succeeded))
            return OrderErrors.InvalidPaymentStatusTransition;

        if (OrderStatus is OrderStatus.Shipped or OrderStatus.Delivered)
            return OrderErrors.RefundNotAllowed;

        PaymentStatus = PaymentStatus.Refunded;
        OrderStatus = OrderStatus.Cancelled;
        RefundId = refundId;
        RefundedAtUtc = utcNow;
        CancelledAtUtc ??= utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public Result RevertRefundPendingToSucceeded(DateTime utcNow)
    {
        if (PaymentStatus != PaymentStatus.RefundPending)
            return OrderErrors.InvalidPaymentStatusTransition;

        PaymentStatus = PaymentStatus.Succeeded;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public Result AdvanceFulfillment(OrderStatus nextStatus, DateTime utcNow)
    {
        var allowed = (OrderStatus, nextStatus) switch
        {
            (OrderStatus.Paid, OrderStatus.Processing) => true,
            (OrderStatus.Processing, OrderStatus.Shipped) => true,
            (OrderStatus.Shipped, OrderStatus.Delivered) => true,
            _ => false
        };

        if (!allowed)
            return OrderErrors.InvalidStatusTransition;

        OrderStatus = nextStatus;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public bool CanBeCancelledByCustomer() =>
        OrderStatus == OrderStatus.Pending &&
        PaymentStatus is not (PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.Refunded);

    public bool IsPaid() =>
        PaymentStatus is PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.Refunded;
}
