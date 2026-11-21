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
        /// Deletes the basket only when its revision still matches, so a guest
        /// basket changed during a merge is never silently discarded.
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
