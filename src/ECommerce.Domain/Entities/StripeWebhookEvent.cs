namespace ECommerce.Domain.Entities;

public sealed class StripeWebhookEvent : AuditedEntity
{
    public string StripeEventId { get; private set; } = default!;
    public string EventType { get; private set; } = default!;
    public DateTime ProcessedAtUtc { get; private set; }
    public string ProcessingStatus { get; private set; } = default!;
    public string? ErrorMessage { get; private set; }

    private StripeWebhookEvent() { }

    public StripeWebhookEvent(string stripeEventId, string eventType, DateTime utcNow)
    {
        Id = Guid.NewGuid();
        StripeEventId = stripeEventId;
        EventType = eventType;
        ProcessedAtUtc = utcNow;
        ProcessingStatus = "Processing";
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    public void MarkProcessed(DateTime utcNow)
    {
        ProcessingStatus = "Processed";
        ProcessedAtUtc = utcNow;
        ErrorMessage = null;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    public void MarkFailed(string errorMessage, DateTime utcNow)
    {
        ProcessingStatus = "Failed";
        ErrorMessage = errorMessage.Length > 1000 ? errorMessage[..1000] : errorMessage;
        ProcessedAtUtc = utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }
}
