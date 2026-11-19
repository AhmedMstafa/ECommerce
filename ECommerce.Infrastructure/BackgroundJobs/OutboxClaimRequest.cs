using ECommerce.Domain.Entities;

namespace ECommerce.Infrastructure.BackgroundJobs;

public sealed record OutboxClaimRequest(
    int BatchSize,
    string LeaseOwner,
    TimeSpan LeaseDuration,
    DateTime UtcNow);

/// <summary>
/// Keeps SQL Server claim mechanics out of the job orchestration so the job can
/// be unit tested without a database.
/// </summary>
public interface IOutboxMessageStore
{
    Task<IReadOnlyList<OutboxMessage>> ClaimAsync(
        OutboxClaimRequest request,
        CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
