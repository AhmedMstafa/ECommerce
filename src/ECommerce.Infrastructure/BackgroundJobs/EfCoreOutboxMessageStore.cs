using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Infrastructure.BackgroundJobs;

/// <summary>
/// Claims outbox messages with a conditional bulk update. Two workers may read
/// the same candidate IDs, but only one update can satisfy the original-state
/// predicate, so a message cannot be handed to two workers at once.
/// </summary>
public sealed class EfCoreOutboxMessageStore(StoreDbContext dbContext) : IOutboxMessageStore
{
    public async Task<IReadOnlyList<OutboxMessage>> ClaimAsync(
        OutboxClaimRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BatchSize <= 0 || string.IsNullOrWhiteSpace(request.LeaseOwner))
        {
            return [];
        }

        var now = request.UtcNow;
        var leaseExpiresAt = now.Add(request.LeaseDuration);

        var candidateIds = await dbContext.OutboxMessages
            .Where(message =>
                (message.Status == OutboxMessageStatus.Pending &&
                 (message.NextAttemptAtUtc == null || message.NextAttemptAtUtc <= now)) ||
                (message.Status == OutboxMessageStatus.Processing &&
                 message.LeaseExpiresAtUtc != null &&
                 message.LeaseExpiresAtUtc < now))
            .OrderBy(message => message.OccurredOnUtc)
            .ThenBy(message => message.Id)
            .Take(request.BatchSize)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0)
        {
            return [];
        }

        var claimedCount = await dbContext.OutboxMessages
            .Where(message =>
                candidateIds.Contains(message.Id) &&
                ((message.Status == OutboxMessageStatus.Pending &&
                  (message.NextAttemptAtUtc == null || message.NextAttemptAtUtc <= now)) ||
                 (message.Status == OutboxMessageStatus.Processing &&
                  message.LeaseExpiresAtUtc != null &&
                  message.LeaseExpiresAtUtc < now)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.Status, OutboxMessageStatus.Processing)
                    .SetProperty(message => message.LeaseOwner, request.LeaseOwner)
                    .SetProperty(message => message.LeaseExpiresAtUtc, leaseExpiresAt)
                    .SetProperty(message => message.LastAttemptAtUtc, now)
                    .SetProperty(
                        message => message.UpdatedAt,
                        new DateTimeOffset(now, TimeSpan.Zero)),
                cancellationToken);

        if (claimedCount == 0)
        {
            return [];
        }

        return await dbContext.OutboxMessages
            .Where(message =>
                message.LeaseOwner == request.LeaseOwner &&
                message.Status == OutboxMessageStatus.Processing &&
                message.LeaseExpiresAtUtc > now)
            .OrderBy(message => message.OccurredOnUtc)
            .ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
