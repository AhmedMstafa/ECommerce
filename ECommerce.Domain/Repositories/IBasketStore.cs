using ECommerce.Domain.Entities;

namespace ECommerce.Domain.Repositories
{
    /// <summary>
    /// Redis-backed basket storage. This is not an EF repository: baskets are
    /// temporary state and are never persisted to SQL Server.
    /// </summary>
    public interface IBasketStore
    {
        Task<Basket?> GetAsync(BasketKey key, CancellationToken ct = default);

        /// <summary>
        /// Atomically writes the basket only when its revision still matches the
        /// value read by the caller. A conflict returns the current document.
        /// </summary>
        Task<BasketSaveResult> SaveAsync(Basket basket, TimeSpan ttl, CancellationToken ct = default);

        /// <summary>
        /// Deletes the basket only when its revision still matches, so a basket
        /// changed concurrently is never silently discarded. Returns <c>true</c> when
        /// the key was deleted or is already absent, and <c>false</c> on a revision
        /// (CAS) conflict. Storage/infrastructure failures must throw
        /// <see cref="BasketStorageException"/>; cancellation must be propagated.
        /// </summary>
        Task<bool> DeleteAsync(BasketKey key, long expectedRevision, CancellationToken ct = default);
    }

    public enum BasketSaveOutcome
    {
        Saved,
        Conflict,
        Failed
    }

    public sealed record BasketSaveResult(BasketSaveOutcome Outcome, Basket? Current);

}
