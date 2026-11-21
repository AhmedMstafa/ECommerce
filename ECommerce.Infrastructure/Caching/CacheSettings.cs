using System.ComponentModel.DataAnnotations;

namespace ECommerce.Infrastructure.Caching;

/// <summary>
/// Configures HybridCache expiration behavior. These values bound how long
/// entries can live in-process (L1) and in the distributed Redis store (L2)
/// without requiring code changes between local Docker Redis and remote Redis.
/// </summary>
public sealed class CacheSettings
{
    public const string SectionName = "Cache";

    /// <summary>
    /// Default overall/distributed expiration applied when a caller does not
    /// specify an explicit expiration.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "30.00:00:00")]
    public TimeSpan DefaultExpiration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum lifetime for the local in-memory (L1) cache entry. Kept at or
    /// below the distributed lifetime to avoid serving stale L1 data after the
    /// distributed entry expires.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan LocalCacheExpiration { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Maximum lifetime for the distributed Redis (L2) cache entry when no
    /// per-call expiration is supplied.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "30.00:00:00")]
    public TimeSpan DistributedCacheExpiration { get; init; } = TimeSpan.FromMinutes(5);

    public void Validate()
    {
        if (LocalCacheExpiration > DistributedCacheExpiration)
        {
            throw new InvalidOperationException(
                "Cache:LocalCacheExpiration must not exceed Cache:DistributedCacheExpiration.");
        }
    }

    public TimeSpan EffectiveLocal(TimeSpan? requested) =>
        MinPositive(requested ?? DefaultExpiration, LocalCacheExpiration);

    public TimeSpan EffectiveDistributed(TimeSpan? requested) =>
        requested ?? DistributedCacheExpiration;

    private static TimeSpan MinPositive(TimeSpan a, TimeSpan b)
    {
        if (a <= TimeSpan.Zero)
        {
            return b;
        }

        if (b <= TimeSpan.Zero)
        {
            return a;
        }

        return a <= b ? a : b;
    }
}
