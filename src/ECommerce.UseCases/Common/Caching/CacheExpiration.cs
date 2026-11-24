namespace ECommerce.UseCases.Common.Caching;

/// <summary>
/// Central expiration policy for product read caches. Values are absolute
/// lifetimes (not sliding) so a popular key cannot stay stale forever.
/// Listings change more often than an individual product, so they expire sooner;
/// detail entries carry a longer TTL. This bounds staleness without adding
/// cache-invalidation coupling to write commands.
/// </summary>
public static class CacheExpiration
{
    public static readonly TimeSpan ProductList = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan ProductDetails = TimeSpan.FromMinutes(5);
}
