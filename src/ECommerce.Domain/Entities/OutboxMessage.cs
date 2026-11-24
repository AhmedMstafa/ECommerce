using System.Security.Cryptography;
using System.Text;
using ECommerce.Domain.Common;

namespace ECommerce.Domain.Entities;

public class OutboxMessage : BaseEntity
{
    public const int IdempotencyKeyMaxLength = 200;
    public const int ErrorMessageMaxLength = 2000;

    public string Type { get; private set; } = default!;
    public string Content { get; private set; } = default!;
    public DateTime OccurredOnUtc { get; private set; }
    public DateTime? ProcessedOnUtc { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int RetryCount { get; private set; }

    public OutboxMessageStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public DateTime? LastAttemptAtUtc { get; private set; }

    private OutboxMessage() { }

    private OutboxMessage(
        Guid id,
        string type,
        string content,
        string idempotencyKey)
    {
        Id = id == default ? Guid.NewGuid() : id;
        Type = type;
        Content = content;
        IdempotencyKey = idempotencyKey;
        OccurredOnUtc = DateTime.UtcNow;
        CreatedAt = OccurredOnUtc;
        UpdatedAt = new DateTimeOffset(OccurredOnUtc, TimeSpan.Zero);
        Status = OutboxMessageStatus.Pending;
        RetryCount = 0;
    }

    public static Result<OutboxMessage> Create(
        string type,
        string content,
        Guid id = default,
        string? idempotencyKey = null)
    {
        if (string.IsNullOrWhiteSpace(type))
            return Result.Failure<OutboxMessage>(Error.Validation("Outbox.EmptyType", "Message type cannot be empty."));

        if (string.IsNullOrWhiteSpace(content))
            return Result.Failure<OutboxMessage>(Error.Validation("Outbox.EmptyContent", "Message content cannot be empty."));

        return new OutboxMessage(id, type, content, DeriveIdempotencyKey(type, content, idempotencyKey));
    }

    /// <summary>
    /// A message is claimable when it has never been attempted, its retry delay
    /// has elapsed, or a previous worker's lease has expired.
    /// </summary>
    public bool CanBeClaimed(DateTime utcNow)
    {
        if (IsDeleted || Status is OutboxMessageStatus.Processed or OutboxMessageStatus.Failed)
        {
            return false;
        }

        return Status switch
        {
            OutboxMessageStatus.Pending =>
                NextAttemptAtUtc is null || NextAttemptAtUtc <= utcNow,
            OutboxMessageStatus.Processing =>
                LeaseExpiresAtUtc is not null && LeaseExpiresAtUtc < utcNow,
            _ => false
        };
    }

    public void MarkAsProcessing(string leaseOwner, TimeSpan leaseDuration, DateTime utcNow)
    {
        EnsureNotFinalized();

        if (string.IsNullOrWhiteSpace(leaseOwner))
            throw new ArgumentException("A lease owner is required.", nameof(leaseOwner));

        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");

        Status = OutboxMessageStatus.Processing;
        LeaseOwner = leaseOwner;
        LeaseExpiresAtUtc = utcNow.Add(leaseDuration);
        LastAttemptAtUtc = utcNow;
        UpdatedAt = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    public void MarkAsProcessed(DateTime utcNow)
    {
        EnsureNotFinalized();

        Status = OutboxMessageStatus.Processed;
        ProcessedOnUtc = utcNow;
        ErrorMessage = null;
        NextAttemptAtUtc = null;
        ClearLease();
        UpdatedAt = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    /// <summary>
    /// Records a transient failure. The message stays retryable and returns to
    /// the pending queue only after <paramref name="retryAtUtc"/>.
    /// </summary>
    public void MarkAsFailed(string error, DateTime retryAtUtc)
    {
        EnsureNotFinalized();

        Status = OutboxMessageStatus.Pending;
        ErrorMessage = TruncateError(error);
        RetryCount++;
        NextAttemptAtUtc = retryAtUtc;
        ClearLease();
        UpdatedAt = new DateTimeOffset(retryAtUtc, TimeSpan.Zero);
    }

    /// <summary>
    /// Terminal state for messages that exhausted their retry budget or are
    /// invalid. Nothing is deleted, so operators can inspect and replay them.
    /// </summary>
    public void MarkAsDeadLettered(string error)
    {
        EnsureNotFinalized();

        Status = OutboxMessageStatus.Failed;
        ErrorMessage = TruncateError(error);
        RetryCount++;
        NextAttemptAtUtc = null;
        ClearLease();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ReleaseLease(DateTime utcNow)
    {
        if (Status != OutboxMessageStatus.Processing)
        {
            return;
        }

        Status = OutboxMessageStatus.Pending;
        ClearLease();
        UpdatedAt = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    private void EnsureNotFinalized()
    {
        if (Status is OutboxMessageStatus.Processed or OutboxMessageStatus.Failed)
        {
            throw new InvalidOperationException(
                $"Cannot change a {Status.ToString().ToLowerInvariant()} outbox message.");
        }
    }

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }

    private static string TruncateError(string? error)
    {
        var value = string.IsNullOrWhiteSpace(error) ? "Unknown outbox processing error." : error.Trim();
        return value.Length <= ErrorMessageMaxLength
            ? value
            : value[..ErrorMessageMaxLength];
    }

    private static string DeriveIdempotencyKey(string type, string content, string? suppliedKey)
    {
        var source = suppliedKey is null
            ? $"{type}\0{content}"
            : $"explicit\0{suppliedKey}";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
