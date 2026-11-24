using System.Collections.Concurrent;
using System.Globalization;
using ECommerce.UseCases.Common.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ECommerce.Infrastructure.Caching;

/// <summary>
/// HybridCache-backed implementation of the application cache abstraction.
/// L1 is process-local memory managed by HybridCache, L2 is the shared Redis
/// instance exposed through IDistributedCache on the existing multiplexer.
/// Redis/cache failures are treated as cache misses (fail-open) so an outage
/// never fails an otherwise valid request.
/// </summary>
public sealed class HybridCacheService(
    HybridCache hybridCache,
    IDistributedCache distributedCache,
    IConnectionMultiplexer multiplexer,
    IOptions<RedisCacheOptions> redisOptions,
    IOptions<CacheSettings> cacheSettings,
    ILogger<HybridCacheService> logger) : ICacheService
{
    private readonly HybridCache _hybridCache = hybridCache;
    private readonly IDistributedCache _distributedCache = distributedCache;
    private readonly IDatabase _database = multiplexer.GetDatabase();
    private readonly CacheSettings _settings = cacheSettings.Value;
    private readonly ILogger<HybridCacheService> _logger = logger;
    private readonly string _instance = redisOptions.Value.InstanceName ?? "ECommerce:";

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            // IDistributedCache implementations (StackExchangeRedisCache) apply
            // InstanceName internally, so pass the logical key.
            var bytes = await _distributedCache.GetAsync(key, ct).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                return default;
            }

            return Deserialize<T>(bytes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailure("read", key, ex);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _settings.EffectiveDistributed(expiration)
            };

            await _distributedCache.SetAsync(key, Serialize(value), options, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailure("write", key, ex);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            // HybridCache manages its own L1/L2 entries; remove best-effort.
            await _hybridCache.RemoveAsync(key, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Fall back to direct distributed removal.
        }

        try
        {
            await _distributedCache.RemoveAsync(key, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailure("remove", key, ex);
        }
    }

    /// <summary>
    /// Invalidates a whole logical scope by bumping its generation counter in
    /// Redis and evicting tagged HybridCache entries. Subsequent reads resolve a
    /// new effective key/generation so stale values are never served.
    /// </summary>
    public async Task RemoveByPrefixAsync(string prefixKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            await _database.StringIncrementAsync(GenerationKey(prefixKey)).WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailure("invalidate", prefixKey, ex);
        }

        try
        {
            await _hybridCache.RemoveByTagAsync(ScopeTag(prefixKey), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Tag eviction is best-effort; generation bump remains the source of
            // logical invalidation for read-through entries.
            LogFailure("invalidate-tags", prefixKey, ex);
        }
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var scope = GetScope(key);
        var effectiveKey = key;
        string[] tags = [];
        try
        {
            if (scope is not null)
            {
                tags = [ScopeTag(scope)];
                var generation = await GetGenerationAsync(scope, ct).ConfigureAwait(false);
                effectiveKey = BuildVersionedKey(scope, key, generation);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // If generation lookup fails, proceed without scope versioning; tag
            // eviction and future successful invalidations still provide safety.
            LogFailure("read-generation", key, ex);
        }

        var options = new HybridCacheEntryOptions
        {
            Expiration = _settings.EffectiveDistributed(expiration),
            LocalCacheExpiration = _settings.EffectiveLocal(expiration)
        };

        try
        {
            return await _hybridCache.GetOrCreateAsync(
                effectiveKey,
                async token => await factory(token).ConfigureAwait(false),
                options,
                tags,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailure("read-through", key, ex);
            // Cache backend unavailable: serve fresh data without caching.
            return await factory(ct).ConfigureAwait(false);
        }
    }

    private async Task<long> GetGenerationAsync(string scope, CancellationToken ct)
    {
        var value = await _database.StringGetAsync(GenerationKey(scope)).WaitAsync(ct).ConfigureAwait(false);
        if (value.IsNullOrEmpty)
        {
            return 0;
        }

        return (long)value;
    }

    private string GenerationKey(string scope) => $"{_instance}__gen:{scope.ToLowerInvariant()}";

    private static string ScopeTag(string scope) => $"scope:{scope.ToLowerInvariant()}";

    private static string? GetScope(string key)
    {
        // Cache scopes are the first logical segment (e.g. "products", "brands").
        var separator = key.IndexOf(':');
        if (separator <= 0)
        {
            return null;
        }

        return key[..separator];
    }

    private static string BuildVersionedKey(string scope, string key, long generation)
    {
        // Keep scope segment stable and append version token to the suffix so
        // scope-based prefix invalidation remains deterministic.
        var separator = key.IndexOf(':');
        var suffix = separator >= 0 && separator < key.Length - 1 ? key[(separator + 1)..] : key;
        return string.Concat(scope, "__v", generation.ToString(CultureInfo.InvariantCulture), ":", suffix);
    }

    private static byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);

    private static T? Deserialize<T>(byte[] bytes) => System.Text.Json.JsonSerializer.Deserialize<T>(bytes);

    private void LogFailure(string operation, string key, Exception ex)
    {
        _logger.LogWarning(
            "Cache {Operation} failed for {KeyScope} ({ErrorType}). Serving fallback path.",
            operation,
            GetScope(key) ?? key,
            ex.GetType().Name);
    }
}
