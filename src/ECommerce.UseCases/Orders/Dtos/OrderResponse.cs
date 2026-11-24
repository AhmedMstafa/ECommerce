namespace ECommerce.UseCases.Orders.Dtos;

public sealed record OrderResponse(
    Guid Id,
    Guid BuyerId,
    string OrderStatus,
    string PaymentStatus,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal Total,
    string DeliveryMethodName,
    decimal DeliveryMethodPrice,
    OrderShippingAddressResponse ShippingAddress,
    IReadOnlyList<OrderItemResponse> Items,
    string? PaymentIntentId,
    string? ClientSecret,
    bool RequiresReconciliation,
    string? ReconciliationReason,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    DateTime? CancelledAtUtc,
    DateTime? RefundedAtUtc);
