namespace ECommerce.Domain.Enums;

public enum PaymentStatus
{
    Pending = 0,
    RequiresAction = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    RefundPending = 5,
    Refunded = 6
}
