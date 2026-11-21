using System.Collections.Concurrent;
using System.Globalization;
using ECommerce.Infrastructure.Caching;
using ECommerce.UseCases.Common.Caching;
using ECommerce.UseCases.Products.Caching;
using ECommerce.UseCases.Products.Specifications;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;

namespace ECommerce.UnitTests;

/// <summary>
/// Redis cache behaviour is exercised through IDistributedCache and a stubbed
/// IConnectionMultiplexer, so no Upstash account or local Redis is required.
/// The stub models the Lua read script against the same generation keys and
/// hash field layout used by the real implementation.
/// </summary>
public class CacheServiceTests
{
    [Fact]
    public async Task GetAsync_ShouldResolveGenerationSpecificKeyInOneRedisOperation()
    {
        var (service, cache, _, redis) = CreateService();
        await service.SetAsync("products:1", "value");
        cache.GetCalls.Should().Be(0);
        redis.ScriptCalls = 0;

        (await service.GetAsync<string>("products:1")).Should().Be("value");

        redis.ScriptCalls.Should().Be(1);
        cache.GetCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_ShouldHonourCurrentGeneration_AfterInvalidation()
    {
        var (service, _, _, redis) = CreateService();
        await service.SetAsync("products:1", "stale");
        redis.ScriptCalls = 0;

        await service.RemoveByPrefixAsync(CacheScopes.Products);

        (await service.GetAsync<string>("products:1")).Should().BeNull();
        redis.ScriptCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldLoadOnceUnderConcurrency()
    {
        var (service, cache, _, _) = CreateService();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;

        Task<string> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref factoryCalls);
            started.TrySetResult();
            return WaitForAsync(release.Task, "value");
        }

        var first = service.GetOrCreateAsync("key", Factory);
        await started.Task;

        var concurrent = Enumerable.Range(0, 9)
            .Select(_ => service.GetOrCreateAsync("key", Factory))
            .ToArray();

        // Give all callers a chance to join the existing flight before releasing.
        await Task.Delay(100);
        release.SetResult();

        var values = await Task.WhenAll(concurrent.Prepend(first));

        values.Should().AllBe("value");
        factoryCalls.Should().Be(1);
        (await service.GetAsync<string>("key")).Should().Be("value");
        cache.Entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldAllowOneCallerToCancelWithoutCancellingSharedWork()
    {
        var (service, _, _, _) = CreateService();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        var factoryToken = CancellationToken.None;

        async Task<string> Factory(CancellationToken token)
        {
            Interlocked.Increment(ref factoryCalls);
            factoryToken = token;
            started.SetResult();
            await release.Task;
            return "value";
        }

        var first = service.GetOrCreateAsync("key", Factory);
        await started.Task;

        using var cancelled = new CancellationTokenSource();
        var second = service.GetOrCreateAsync("key", Factory, ct: cancelled.Token);
        await Task.Delay(100);
        cancelled.Cancel();
        release.SetResult();

        await second.Invoking(_ => second)
            .Should().ThrowAsync<OperationCanceledException>();
        (await first).Should().Be("value");

        factoryCalls.Should().Be(1);
        factoryToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_ShouldRespectCancellation_BeforeReading()
    {
        var (service, _, _, redis) = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.Invoking(s => s.GetAsync<string>("key", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        redis.ScriptCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldServeSecondCallFromCache()
    {
        var (service, cache, _, _) = CreateService();
        var calls = 0;

        Task<string> Factory(CancellationToken _)
        {
            calls++;
            return Task.FromResult("value");
        }

        (await service.GetOrCreateAsync("key", Factory)).Should().Be("value");
        (await service.GetOrCreateAsync("key", Factory)).Should().Be("value");

        calls.Should().Be(1);
        cache.Entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldNotCacheFailedFactory()
    {
        var (service, _, _, _) = CreateService();
        var calls = 0;

        Task<string> Failing(CancellationToken _) =>
            Task.FromException<string>(new InvalidOperationException("boom"));

        await service.Invoking(s => s.GetOrCreateAsync("key", Failing))
            .Should().ThrowAsync<InvalidOperationException>();

        var recovered = await service.GetOrCreateAsync("key", _ =>
        {
            calls++;
            return Task.FromResult("recovered");
        });

        recovered.Should().Be("recovered");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldCacheNullReferenceResults()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.GetOrCreateAsync<string?>("key", _ => Task.FromResult<string?>(null));
        result.Should().BeNull();

        var extraCalls = 0;
        (await service.GetOrCreateAsync<string?>("key", _ =>
        {
            extraCalls++;
            return Task.FromResult<string?>("not used");
        })).Should().BeNull();

        extraCalls.Should().Be(0);
    }

    [Fact]
    public async Task SetAsync_ShouldApplyConfiguredTtl()
    {
        var (service, cache, _, _) = CreateService();

        await service.SetAsync("key", "value", TimeSpan.FromSeconds(42));

        // The InstanceName prefix is added by the real IDistributedCache.
        var options = cache.Options.Should().ContainKey("key|g0").WhoseValue;
        options.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public async Task SetAsync_ShouldUseFallbackTtl_WhenNoExpirationGiven()
    {
        var (service, cache, _, _) = CreateService();

        await service.SetAsync("key", "value");

        cache.Options["key|g0"].AbsoluteExpirationRelativeToNow
            .Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task SetAsync_ShouldAppendGenerationToLogicalRedisKey()
    {
        var (service, cache, _, _) = CreateService();

        await service.SetAsync("Products:Id:abc", "value");

        // The service owns the generation suffix; IDistributedCache owns the
        // instance-name prefix.
        cache.Entries.Keys.Should().ContainSingle()
            .Which.Should().Be("Products:Id:abc|g0");
    }

    [Fact]
    public async Task GetAsync_ShouldTreatExpiredEntryAsMiss()
    {
        var (service, cache, _, _) = CreateService();
        await service.SetAsync("key", "value", TimeSpan.FromMilliseconds(1));

        // Redis removes the hash when its TTL elapses; emulate that here.
        cache.Expire("key|g0");

        (await service.GetAsync<string>("key")).Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldStoreSeparateEntries_ForDifferentPagingParameters()
    {
        var (service, cache, _, _) = CreateService();

        await service.GetOrCreateAsync(
            ProductCacheKeys.List(new ProductSpecificationParameters { PageNumber = 1, PageSize = 10 }),
            _ => Task.FromResult("page 1"));
        await service.GetOrCreateAsync(
            ProductCacheKeys.List(new ProductSpecificationParameters { PageNumber = 2, PageSize = 10 }),
            _ => Task.FromResult("page 2"));

        cache.Entries.Should().HaveCount(2);
        cache.Entries.Keys.Should().OnlyContain(key => key.StartsWith("Products:Page:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemoveByPrefixAsync_ShouldInvalidateAllProductPagesAndDetails()
    {
        var (service, cache, _, _) = CreateService();
        var listKey = ProductCacheKeys.List(new ProductSpecificationParameters());
        var detailsKey = ProductCacheKeys.Details(Guid.NewGuid());

        await service.SetAsync(listKey, "list");
        await service.SetAsync(detailsKey, "details");
        cache.Entries.Should().HaveCount(2);

        await service.RemoveByPrefixAsync(CacheScopes.Products);

        (await service.GetAsync<string>(listKey)).Should().BeNull();
        (await service.GetAsync<string>(detailsKey)).Should().BeNull();

        // Generation invalidation never calls KEYS/SCAN: old Redis entries stay
        // until their TTL but are no longer reachable through the new generation.
        cache.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task RemoveByPrefixAsync_ShouldNotAffectOtherScopes()
    {
        var (service, _, _, _) = CreateService();

        await service.SetAsync("products:1", "product");
        await service.SetAsync("brands:all", "brands");

        await service.RemoveByPrefixAsync(CacheScopes.Products);

        (await service.GetAsync<string>("products:1")).Should().BeNull();
        (await service.GetAsync<string>("brands:all")).Should().Be("brands");
    }

    [Fact]
    public async Task GetOrCreateAsync_WithInvalidationDuringLoad_ShouldNotServeStaleValueOnNextRead()
    {
        var (service, _, _, _) = CreateService();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var load = service.GetOrCreateAsync("products:1", async _ =>
        {
            started.SetResult();
            await release.Task;
            return "stale";
        });
        await started.Task;

        await service.RemoveByPrefixAsync(CacheScopes.Products);
        release.SetResult();

        (await load).Should().Be("stale");
        (await service.GetAsync<string>("products:1")).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ShouldNotServeValue_InvalidatedDuringAtomicRead()
    {
        var (service, _, _, redis) = CreateService();
        await service.SetAsync("products:1", "stale");

        var invalidated = false;
        redis.OnScript = () =>
        {
            if (invalidated)
            {
                return;
            }

            invalidated = true;
            redis.Increment(CacheScopes.Products);
        };

        (await service.GetAsync<string>("products:1")).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenRedisReadFails()
    {
        var (service, _, _, redis) = CreateService();
        redis.ThrowOnScript = true;

        (await service.GetAsync<string>("key")).Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldFallBackToFactory_WhenRedisReadFails()
    {
        var (service, _, _, redis) = CreateService();
        redis.ThrowOnScript = true;
        var factoryCalls = 0;

        var result = await service.GetOrCreateAsync("key", _ =>
        {
            factoryCalls++;
            return Task.FromResult("from-sql");
        });

        result.Should().Be("from-sql");
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldStillWork_WhenGenerationReadFails()
    {
        var (service, _, database, _) = CreateService();
        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<RedisValue>(_ => throw new RedisException("generation read failed"));

        var result = await service.GetOrCreateAsync("key", _ => Task.FromResult("from-sql"));

        result.Should().Be("from-sql");
        (await service.GetAsync<string>("key")).Should().Be("from-sql");
    }

    [Fact]
    public async Task SetAsync_ShouldSwallowRedisFailure()
    {
        var (service, cache, _, _) = CreateService();
        cache.ThrowOnSet = true;

        var act = async () => await service.SetAsync("key", "value");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_ShouldSwallowRedisFailure()
    {
        var (service, _, database, _) = CreateService();
        database.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns<long>(_ => throw new RedisException("invalidate failed"));

        var act = async () => await service.RemoveByPrefixAsync(CacheScopes.Products);

        await act.Should().NotThrowAsync();
    }

    private static (RedisCacheService Service, FakeDistributedCache Cache, IDatabase Database, FakeRedis Redis) CreateService()
    {
        const string instanceName = "ECommerce:";
        var cache = new FakeDistributedCache();
        var (multiplexer, database, redis) = CreateMultiplexer(cache, instanceName);

        var service = new RedisCacheService(
            cache,
            multiplexer,
            Options.Create(new RedisCacheOptions { InstanceName = instanceName }),
            NullLogger<RedisCacheService>.Instance);

        return (service, cache, database, redis);
    }

    private static (IConnectionMultiplexer Multiplexer, IDatabase Database, FakeRedis Redis) CreateMultiplexer(
        FakeDistributedCache cache,
        string instanceName)
    {
        var redis = new FakeRedis(cache, instanceName);
        var database = Substitute.For<IDatabase>();
        var generationPrefix = instanceName + "gen:";

        database.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(call => redis.Evaluate(call.ArgAt<RedisKey[]>(1)));

        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<RedisValue>(call =>
            {
                var key = call.Arg<RedisKey>().ToString();
                var scope = key.StartsWith(generationPrefix, StringComparison.Ordinal)
                    ? key[generationPrefix.Length..]
                    : key;
                return (RedisValue)redis.GetGeneration(scope);
            });

        database.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns<long>(call =>
            {
                var key = call.Arg<RedisKey>().ToString();
                var scope = key.StartsWith(generationPrefix, StringComparison.Ordinal)
                    ? key[generationPrefix.Length..]
                    : key;
                return redis.Increment(scope, call.Arg<long>());
            });

        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.GetDatabase().Returns(database);

        return (multiplexer, database, redis);
    }

    private static async Task<T> WaitForAsync<T>(Task gate, T value)
    {
        await gate;
        return value;
    }

    /// <summary>In-memory IDistributedCache that also captures TTL options.</summary>
    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, DistributedCacheEntryOptions> Options { get; } = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, byte[]> Entries => _entries;

        public int GetCalls;
        public bool ThrowOnSet { get; set; }

        public byte[]? Get(string key) => GetAsync(key, default).GetAwaiter().GetResult();

        public Task<byte[]?> GetAsync(string key, CancellationToken token)
        {
            Interlocked.Increment(ref GetCalls);
            return Task.FromResult(TryGet(key, out var bytes) ? bytes : null);
        }

        public bool TryGet(string key, out byte[]? bytes) => _entries.TryGetValue(key, out bytes);

        public void Expire(string key)
        {
            _entries.TryRemove(key, out _);
            Options.TryRemove(key, out _);
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token) => Task.CompletedTask;

        public void Remove(string key) => RemoveAsync(key, default).GetAwaiter().GetResult();

        public Task RemoveAsync(string key, CancellationToken token)
        {
            _entries.TryRemove(key, out _);
            Options.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            SetAsync(key, value, options, default).GetAwaiter().GetResult();

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token)
        {
            if (ThrowOnSet)
            {
                throw new RedisException("simulated set failure");
            }

            _entries[key] = value;
            Options[key] = options;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Models the generation counter and the Lua script's view of the
    /// IDistributedCache hash layout.
    /// </summary>
    private sealed class FakeRedis(FakeDistributedCache cache, string instanceName)
    {
        private readonly ConcurrentDictionary<string, long> _generations = new(StringComparer.Ordinal);

        public int ScriptCalls;
        public bool ThrowOnScript { get; set; }
        public Action? OnScript { get; set; }

        public long GetGeneration(string scope) =>
            _generations.TryGetValue(scope, out var generation) ? generation : 0;

        public long Increment(string scope, long by = 1)
        {
            var next = GetGeneration(scope) + by;
            _generations[scope] = next;
            return next;
        }

        public Task<RedisResult> Evaluate(RedisKey[] keys)
        {
            Interlocked.Increment(ref ScriptCalls);

            if (ThrowOnScript)
            {
                throw new RedisException("simulated script failure");
            }

            OnScript?.Invoke();

            var generationKey = keys[0].ToString();
            var generationPrefix = instanceName + "gen:";
            var scope = generationKey.StartsWith(generationPrefix, StringComparison.Ordinal)
                ? generationKey[generationPrefix.Length..]
                : generationKey;
            var generation = GetGeneration(scope);

            var dataPrefix = keys[1].ToString();
            var logicalPrefix = dataPrefix.StartsWith(instanceName, StringComparison.Ordinal)
                ? dataPrefix[instanceName.Length..]
                : dataPrefix;
            var dataKey = logicalPrefix + generation.ToString(CultureInfo.InvariantCulture);

            return cache.TryGet(dataKey, out var bytes)
                ? Task.FromResult(RedisResult.Create(new RedisValue[]
                {
                    (RedisValue)generation.ToString(CultureInfo.InvariantCulture),
                    (RedisValue)"1",
                    (RedisValue)bytes!
                }))
                : Task.FromResult(RedisResult.Create(new RedisValue[]
                {
                    (RedisValue)generation.ToString(CultureInfo.InvariantCulture),
                    (RedisValue)"0",
                    (RedisValue)""
                }));
        }
    }
}
