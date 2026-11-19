using System.Collections.Concurrent;
using ECommerce.UseCases.Common.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace ECommerce.Infrastructure.Caching;

/// <summary>
/// In-memory cache with single-flight loading and generation-aware invalidation.
/// Tracking is instance-scoped: static dictionaries leaked keys between tests and
/// between independent cache instances.
/// </summary>
public sealed class MemoryCacheService(IMemoryCache memoryCache) : ICacheService
{
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private long _invalidationGeneration;

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var normalizedKey = NormalizeKey(key);

        if (!memoryCache.TryGetValue(normalizedKey, out var value))
        {
            return Task.FromResult<T?>(default);
        }

        if (value is CacheEntry entry)
        {
            return Task.FromResult(entry.Value is T typed ? typed : default);
        }

        // Tolerate entries written directly to IMemoryCache before this service
        // was introduced, without making them participate in prefix tracking.
        return Task.FromResult(value is T direct ? direct : default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default)
    {
        lock (_sync)
        {
            SetCore(NormalizeKey(key), value, expiration);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        var normalizedKey = NormalizeKey(key);

        lock (_sync)
        {
            Interlocked.Increment(ref _invalidationGeneration);
            RemoveCore(normalizedKey);
            RemoveFlight(normalizedKey);
        }

        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefixKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prefixKey))
        {
            throw new ArgumentException("A cache key prefix is required.", nameof(prefixKey));
        }

        var normalizedPrefix = NormalizeKey(prefixKey);

        lock (_sync)
        {
            // One generation bump covers every matching key. A loader that
            // started before this call cannot write its stale result afterwards.
            Interlocked.Increment(ref _invalidationGeneration);

            var matchingKeys = _keys.Keys
                .Where(key => key.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in matchingKeys)
            {
                RemoveCore(key);
            }

            // A key whose loader is still running may not be tracked yet.
            foreach (var flightKey in _inFlight.Keys
                         .Where(key => key.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                RemoveFlight(flightKey);
            }
        }

        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var normalizedKey = NormalizeKey(key);

        if (TryGetCached(normalizedKey, out T cached))
        {
            return cached;
        }

        var context = new LoadContext<T>(
            this,
            normalizedKey,
            factory,
            expiration,
            Volatile.Read(ref _invalidationGeneration));

        Lazy<Task<object?>>? lazy = null;
        lazy = _inFlight.GetOrAdd(
            normalizedKey,
            _ => new Lazy<Task<object?>>(
                () => context.LoadAsync(lazy!),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            // The shared factory is deliberately not bound to one caller's
            // cancellation token. A cancelled caller stops waiting but does not
            // break every other request waiting for the same value.
            var value = await lazy.Value.WaitAsync(ct);
            return value is T typed ? typed : default!;
        }
        finally
        {
            RemoveFlight(normalizedKey, lazy);
        }
    }

    private bool TryGetCached<T>(string key, out T value)
    {
        if (memoryCache.TryGetValue(key, out var stored))
        {
            if (stored is CacheEntry entry)
            {
                if (entry.Value is T typed)
                {
                    value = typed;
                    return true;
                }

                // A cached null is still a cache hit for nullable reference types.
                if (entry.Value is null && default(T) is null)
                {
                    value = default!;
                    return true;
                }
            }

            if (stored is T direct)
            {
                value = direct;
                return true;
            }
        }

        value = default!;
        return false;
    }

    private void SetCore<T>(string key, T value, TimeSpan? expiration)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration is { } valid && valid > TimeSpan.Zero
                ? valid
                : DefaultExpiration
        };

        options.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
        {
            if (evictedKey is string stringKey)
            {
                _keys.TryRemove(NormalizeKey(stringKey), out _);
            }
        });

        Interlocked.Increment(ref _invalidationGeneration);
        memoryCache.Set(key, new CacheEntry(value), options);
        _keys[key] = 0;
    }

    private bool TryCommit<T>(
        string key,
        T value,
        TimeSpan? expiration,
        long expectedGeneration,
        Lazy<Task<object?>> expectedFlight)
    {
        lock (_sync)
        {
            if (Volatile.Read(ref _invalidationGeneration) != expectedGeneration ||
                !_inFlight.TryGetValue(key, out var current) ||
                !ReferenceEquals(current, expectedFlight))
            {
                return false;
            }

            SetCore(key, value, expiration);
            return true;
        }
    }

    private void RemoveCore(string key)
    {
        memoryCache.Remove(key);
        _keys.TryRemove(key, out _);
    }

    private void RemoveFlight(string key)
    {
        if (_inFlight.TryGetValue(key, out var existing))
        {
            RemoveFlight(key, existing);
        }
    }

    private void RemoveFlight(string key, Lazy<Task<object?>> expected)
    {
        // Atomic remove-if-same-reference: an older loader must not evict a newer
        // flight for the same key when it finally completes.
        ((ICollection<KeyValuePair<string, Lazy<Task<object?>>>>)_inFlight)
            .Remove(new KeyValuePair<string, Lazy<Task<object?>>>(key, expected));
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A cache key is required.", nameof(key));
        }

        return key.Trim().ToLowerInvariant();
    }

    private sealed record CacheEntry(object? Value);

    private sealed class LoadContext<T>(
        MemoryCacheService owner,
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration,
        long generationAtStart)
    {
        public async Task<object?> LoadAsync(Lazy<Task<object?>> flight)
        {
            // Shared work is not tied to one caller. Each caller cancels only its
            // own wait in GetOrCreateAsync.
            var result = await factory(CancellationToken.None);

            // Do not repopulate a key that was removed, overwritten, or covered
            // by a prefix invalidation while the factory was running.
            owner.TryCommit(key, result, expiration, generationAtStart, flight);
            return result;
        }
    }
}
