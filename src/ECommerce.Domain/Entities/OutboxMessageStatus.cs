namespace ECommerce.Domain.Entities;

/// <summary>
/// Explicit lifecycle for an outbox message. Persisted as text so the table is
/// easy to inspect and operate when investigating stuck deliveries.
/// </summary>
public enum OutboxMessageStatus
{
    Pending = 0,
    Processing = 1,
    Processed = 2,
    Failed = 3
}
