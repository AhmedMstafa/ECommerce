using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using ECommerce.UseCases.Common.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ECommerce.Infrastructure.Caching;

/// <summary>
/// IDistributedCache-backed implementation of the application cache abstraction.
/// Redis is a cache, not a source of truth: every failure is logged and degraded
/// to a cache miss so an outage never fails an otherwise valid query.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    // IDistributedCache stores its payload in a hash with a "data" field and
    // applies the TTL to the whole key. Reading that field from Lua keeps the
    // framework's TTL semantics while allowing generation + data to be read in
    // one round trip.
    private const string AtomicReadScript = """
        local generation = redis.call('GET', KEYS[1])
        if not generation or not string.match(generation, '^%d+$') then
            generation = '0'
        end

        local value = redis.call('HGET', KEYS[2] .. generation, 'data')
        if value then
            return { generation, '1', value }
        end

        return { generation, '0', '' }
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan FallbackExpiration = TimeSpan.FromMinutes(10);

    private readonly IDistributedCache _cache;
    private readonly IDatabase _database;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly string _instanceName;
    private readonly string _generationPrefix;

    // Process-local single flight. Values are Lazy<Task<T>> so one caller's
    // cancellation cannot cancel the shared work that other callers need.
    private readonly ConcurrentDictionary<string, object> _flights = new(StringComparer.OrdinalIgnoreCase);

    public RedisCacheService(
        IDistributedCache cache,
        IConnectionMultiplexer multiplexer,
        IOptions<RedisCacheOptions> cacheOptions,
        ILogger<RedisCacheService> logger)
    {
        _cache = cache;
        _database = multiplexer.GetDatabase();
        _logger = logger;
        _instanceName = cacheOptions.Value.InstanceName ?? string.Empty;
        _generationPrefix = _instanceName + "gen:";
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        // The Lua script reads the generation and the generation-specific data
        // atomically, so no second generation check is needed. An invalidation
        // that runs after the script has linearized is observed by the next read.
        var read = await ReadAtomicAsync<T>(key, GetScope(key), ct);
        return read.Found ? read.Value : default;
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken ct = default)
    {
        var fullKey = await BuildKeyAsync(key, ct);
        await SetCoreAsync(fullKey, value, expiration, ct);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        var fullKey = await BuildKeyAsync(key, ct);

        try
        {
            await _cache.RemoveAsync(fullKey, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailure("remove", fullKey, ex);
        }
    }

    /// <summary>
    /// Invalidates a whole scope by atomically bumping its generation counter.
    /// All keys in that scope become unreachable immediately and expire by TTL;
    /// no KEYS/SCAN and no per-key enumeration is needed.
    /// </summary>
    public async Task RemoveByPrefixAsync(string prefixKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prefixKey))
        {
            throw new ArgumentException("A cache key prefix is required.", nameof(prefixKey));
        }

        var scope = GetScope(prefixKey);

        try
        {
            await _database.StringIncrementAsync(_generationPrefix + scope, 1).WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailure("invalidate", scope, ex);
        }
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ct.ThrowIfCancellationRequested();

        var scope = GetScope(key);
        var initial = await ReadAtomicAsync<T>(key, scope, ct);
        if (initial.Found)
        {
            return initial.Value!;
        }

        var fullKey = BuildFullKey(key, initial.Generation);
        var flight = _flights.GetOrAdd(fullKey, _ => new Lazy<Task<T>>(
            () => LoadSharedAsync(key, scope, initial.Generation, factory, expiration),
            LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var pending = (Lazy<Task<T>>)flight;

            // Observe a possible factory fault even when this caller is cancelled
            // before it gets to await the shared task.
            _ = pending.Value.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            // Caller cancellation stops waiting only; it never cancels the
            // factory or cache write shared with the other waiters.
            return await pending.Value.WaitAsync(ct);
        }
        finally
        {
            if (_flights.TryGetValue(fullKey, out var current) && ReferenceEquals(current, flight))
            {
                _flights.TryRemove(fullKey, out _);
            }
        }
    }

    private async Task<T> LoadSharedAsync<T>(
        string key,
        string scope,
        long generation,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration)
    {
        // Replaces the old post-lock GET + generation re-check with one atomic
        // read. It is the same safety check: if another process populated the
        // cache or invalidated the scope while callers queued, do not overwrite
        // or return that stale entry.
        var existing = await ReadAtomicAsync<T>(key, scope, CancellationToken.None);
        if (existing.Found && existing.Generation == generation)
        {
            return existing.Value!;
        }

        // Shared work intentionally uses CancellationToken.None: it belongs to
        // all waiters, not to whichever caller happened to create the flight.
        var value = await factory(CancellationToken.None);
        await SetCoreAsync(BuildFullKey(key, generation), value, expiration, CancellationToken.None);
        return value;
    }

    private async Task SetCoreAsync<T>(
        string fullKey,
        T value,
        TimeSpan? expiration,
        CancellationToken ct)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new CacheEnvelope<T>(true, value), Json);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration is { } ttl && ttl > TimeSpan.Zero
                    ? ttl
                    : FallbackExpiration
            };

            await _cache.SetAsync(fullKey, bytes, options, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailure("set", fullKey, ex);
        }
    }

    private async Task<CacheRead<T>> ReadAtomicAsync<T>(string logicalKey, string scope, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            // KEYS[1] is the generation counter; KEYS[2] is the physical data
            // key prefix. Lua appends only a counter value that it has validated
            // as digits, so no caller-controlled text is injected into the key.
            var keys = new RedisKey[]
            {
                _generationPrefix + scope,
                _instanceName + logicalKey + "|g"
            };

            var result = await _database
                .ScriptEvaluateAsync(AtomicReadScript, keys, Array.Empty<RedisValue>())
                .WaitAsync(ct);

            if (result.IsNull || result.Length != 3)
            {
                throw new InvalidOperationException("Unexpected result from the cache read script.");
            }

            var generationText = (string?)result[0];
            var generation = long.TryParse(generationText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;

            var found = string.Equals((string?)result[1], "1", StringComparison.Ordinal);
            var bytes = found ? (byte[]?)result[2] : null;

            if (bytes is null)
            {
                return new CacheRead<T>(false, default, generation);
            }

            var envelope = JsonSerializer.Deserialize<CacheEnvelope<T>>(bytes, Json);
            if (envelope is { HasValue: true })
            {
                return new CacheRead<T>(true, envelope.Value, generation);
            }

            // A cached null is only a usable result for nullable T.
            return new CacheRead<T>(default(T) is null, default, generation);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Script failures, malformed JSON and connection failures are all
            // treated as misses. Never let a cache read fail the query.
            LogFailure("get", logicalKey, ex);
            return new CacheRead<T>(false, default, 0);
        }
    }

    private async Task<string> BuildKeyAsync(string logicalKey, CancellationToken ct)
    {
        var generation = await GetGenerationAsync(GetScope(logicalKey), ct);
        return BuildFullKey(logicalKey, generation);
    }

    private static string BuildFullKey(string logicalKey, long generation) =>
        $"{logicalKey}|g{generation.ToString(CultureInfo.InvariantCulture)}";

    private async Task<long> GetGenerationAsync(string scope, CancellationToken ct)
    {
        try
        {
            var value = await _database.StringGetAsync(_generationPrefix + scope).WaitAsync(ct);

            return value.HasValue && long.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
                ? generation
                : 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailure("generation-read", scope, ex);
            return 0;
        }
    }

    private static string GetScope(string key)
    {
        var separator = key.IndexOf(':');
        var scope = separator > 0 ? key[..separator] : key;
        return scope.Trim().ToLowerInvariant();
    }

    private void LogFailure(string operation, string key, Exception ex)
    {
        // No exception object and no connection string: logs carry only the
        // operation, logical key and error type.
        _logger.LogWarning(
            "Redis cache {Operation} failed for {CacheKey} ({ErrorType}); continuing without cache.",
            operation,
            key,
            ex.GetType().Name);
    }
}

/// <summary>Result of an atomic cache read, including the generation it observed.</summary>
internal readonly record struct CacheRead<T>(bool Found, T? Value, long Generation);

/// <summary>Wrapper that makes a cached null distinguishable from a cache miss.</summary>
public sealed record CacheEnvelope<T>(bool HasValue, T? Value);
