namespace ECommerce.Domain.Common;

public static class OrderErrors
{
    public static Error NotFound(Guid id) =>
        Error.NotFound("Order.NotFound", $"Order with id '{id}' was not found.");

    public static Error NotFoundByPaymentIntent(string paymentIntentId) =>
        Error.NotFound("Order.NotFoundByPaymentIntent", $"Order with payment intent '{paymentIntentId}' was not found.");

    public static readonly Error EmptyBasket =
        Error.Validation("Order.EmptyBasket", "The basket is empty.");

    public static readonly Error InvalidAddress =
        Error.Validation("Order.InvalidAddress", "A valid shipping address is required.");

    public static readonly Error AddressNotFound =
        Error.NotFound("Order.AddressNotFound", "The selected address was not found.");

    public static readonly Error AddressNotOwned =
        Error.Forbidden("Order.AddressNotOwned", "The selected address does not belong to the current user.");

    public static readonly Error DeliveryMethodNotFound =
        Error.NotFound("Order.DeliveryMethodNotFound", "The selected delivery method was not found.");

    public static readonly Error DeliveryMethodInactive =
        Error.Validation("Order.DeliveryMethodInactive", "The selected delivery method is not active.");

    public static readonly Error IdempotencyKeyRequired =
        Error.Validation("Order.IdempotencyKeyRequired", "The Idempotency-Key header is required.");

    public static readonly Error IdempotencyKeyTooLong =
        Error.Validation("Order.IdempotencyKeyTooLong", "The Idempotency-Key header must not exceed 128 characters.");

    public static readonly Error ProductUnavailable =
        Error.Conflict("Order.ProductUnavailable", "One or more products in the basket are unavailable.");

    public static readonly Error InsufficientStock =
        Error.Conflict("Order.InsufficientStock", "Insufficient stock for one or more products.");

    public static readonly Error InvalidStatusTransition =
        Error.Conflict("Order.InvalidStatusTransition", "The requested order status transition is not allowed.");

    public static readonly Error InvalidPaymentStatusTransition =
        Error.Conflict("Order.InvalidPaymentStatusTransition", "The requested payment status transition is not allowed.");

    public static readonly Error NotOwner =
        Error.Forbidden("Order.NotOwner", "You do not have access to this order.");

    public static readonly Error CannotCancel =
        Error.Conflict("Order.CannotCancel", "This order cannot be cancelled in its current state.");

    public static readonly Error PaymentAlreadySucceeded =
        Error.Conflict("Order.PaymentAlreadySucceeded", "Payment has already been confirmed for this order.");

    public static readonly Error ReservationNotActive =
        Error.Conflict("Order.ReservationNotActive", "The stock reservation is no longer active.");

    public static readonly Error RefundNotAllowed =
        Error.Conflict("Order.RefundNotAllowed", "This order is not eligible for a refund.");

    public static readonly Error PaymentIntentCreationFailed =
        Error.Failure("Order.PaymentIntentCreationFailed", "Failed to create a payment intent. Please try again.");

    public static readonly Error ConcurrentUpdate =
        Error.Conflict("Order.ConcurrentUpdate", "The order was changed by another request. Please retry.");

    public static readonly Error ReconciliationRequired =
        Error.Conflict("Order.ReconciliationRequired", "This order requires reconciliation.");
}
