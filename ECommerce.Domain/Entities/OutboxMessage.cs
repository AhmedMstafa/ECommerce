using ECommerce.Domain.Common;

namespace ECommerce.Domain.Entities;

public class OutboxMessage : BaseEntity
{
    public string Type { get; private set; } = default!;
    public string Content { get; private set; } = default!;
    public DateTime OccurredOnUtc { get; private set; }
    public DateTime? ProcessedOnUtc { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int RetryCount { get; private set; }

    private OutboxMessage() { }

    private OutboxMessage(Guid id, string type, string content)
    {
        Id = id == default ? Guid.NewGuid() : id;
        Type = type;
        Content = content;
        OccurredOnUtc = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
        RetryCount = 0;
    }

    public static Result<OutboxMessage> Create(string type, string content, Guid id = default)
    {
        if (string.IsNullOrWhiteSpace(type))
            return Result.Failure<OutboxMessage>(Error.Validation("Outbox.EmptyType", "Message type cannot be empty."));

        if (string.IsNullOrWhiteSpace(content))
            return Result.Failure<OutboxMessage>(Error.Validation("Outbox.EmptyContent", "Message content cannot be empty."));

        return new OutboxMessage(id, type, content);
    }

    public void MarkAsProcessed()
    {
        ProcessedOnUtc = DateTime.UtcNow;
        ErrorMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsFailed(string error)
    {
        ErrorMessage = error;
        RetryCount++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
