using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using ECommerce.Infrastructure.Caching;
using ECommerce.UseCases.Common.Caching;
using ECommerce.UseCases.Products.Caching;
using ECommerce.UseCases.Products.Specifications;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;

namespace ECommerce.UnitTests;

/// <summary>
/// Behavioral tests for the HybridCache-backed application cache service.
/// Redis/network access is avoided by using in-memory and stubbed dependencies.
/// </summary>
public class CacheServiceTests
{
    [Fact]
    public async Task GetOrCreateAsync_ShouldLoadOnceOnCacheMiss_AndServeFromL1CacheHit()
    {
        await using var ctx = CreateContext();
        var calls = 0;

        Task<string> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult("value");
        }

        (await ctx.Service.GetOrCreateAsync("products:1", Factory, TimeSpan.FromMinutes(5))).Should().Be("value");
        (await ctx.Service.GetOrCreateAsync("products:1", Factory, TimeSpan.FromMinutes(5))).Should().Be("value");

        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldServeFromL2_WhenLocalCacheIsBypassed()
    {
        await using var producer = CreateContext();
        var calls = 0;

        (await producer.Service.GetOrCreateAsync("products:l2", _ =>
        {
            calls++;
            return Task.FromResult(new Payload("l2", 7));
        }, TimeSpan.FromMinutes(5))).Should().BeEquivalentTo(new Payload("l2", 7));

        // New HybridCache instance = empty L1, but same distributed cache (L2).
        await using var consumer = CreateContext(producer.Distributed, producer.Redis);

        var fromL2 = await consumer.Service.GetOrCreateAsync("products:l2", _ =>
        {
            calls++;
            return Task.FromResult(new Payload("factory", 0));
        }, TimeSpan.FromMinutes(5));

        fromL2.Should().BeEquivalentTo(new Payload("l2", 7));
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldMiss_WhenDistributedEntryExpired()
    {
        await using var ctx = CreateContext();
        var calls = 0;

        await ctx.Service.GetOrCreateAsync("products:ttl", _ =>
        {
            calls++;
            return Task.FromResult("first");
        }, TimeSpan.FromMilliseconds(50));

        // Force expiration by clearing distributed storage (simulates Redis TTL expiry).
        ctx.Distributed.Clear();
        await Task.Delay(10);

        // New instance to avoid serving from L1 after simulated L2 expiry.
        await using var fresh = CreateContext(ctx.Distributed, ctx.Redis);
        var value = await fresh.Service.GetOrCreateAsync("products:ttl", _ =>
        {
            calls++;
            return Task.FromResult("second");
        }, TimeSpan.FromMinutes(1));

        value.Should().Be("second");
        calls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task RemoveByPrefixAsync_ShouldInvalidateScopedEntries_AndNotAffectOtherScopes()
    {
        await using var ctx = CreateContext();

        await ctx.Service.GetOrCreateAsync("products:1", _ => Task.FromResult("p1"), TimeSpan.FromMinutes(5));
        await ctx.Service.GetOrCreateAsync("brands:all", _ => Task.FromResult("b1"), TimeSpan.FromMinutes(30));

        await ctx.Service.RemoveByPrefixAsync(CacheScopes.Products);

        var productCalls = 0;
        (await ctx.Service.GetOrCreateAsync("products:1", _ =>
        {
            productCalls++;
            return Task.FromResult("p2");
        }, TimeSpan.FromMinutes(5))).Should().Be("p2");
        productCalls.Should().Be(1);

        var brandCalls = 0;
        (await ctx.Service.GetOrCreateAsync("brands:all", _ =>
        {
            brandCalls++;
            return Task.FromResult("b2");
        }, TimeSpan.FromMinutes(30))).Should().Be("b1");
        brandCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldProvideStampedeProtection_UnderConcurrency()
    {
        await using var ctx = CreateContext();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;

        async Task<string> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref factoryCalls);
            started.TrySetResult();
            await release.Task;
            return "value";
        }

        var first = ctx.Service.GetOrCreateAsync("products:stampede", Factory, TimeSpan.FromMinutes(5));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var concurrent = Enumerable.Range(0, 9)
            .Select(_ => ctx.Service.GetOrCreateAsync("products:stampede", Factory, TimeSpan.FromMinutes(5)))
            .ToArray();

        release.SetResult();

        var values = await Task.WhenAll(concurrent.Prepend(first));
        values.Should().AllBe("value");
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldFallbackToFactory_WhenDistributedCacheReadFails()
    {
        var distributed = new ThrowingDistributedCache(throwOnGet: true);
        var redis = new FakeRedisDistributed(distributed);
        var settings = new CacheSettings
        {
            DefaultExpiration = TimeSpan.FromMinutes(5),
            LocalCacheExpiration = TimeSpan.FromMinutes(2),
            DistributedCacheExpiration = TimeSpan.FromMinutes(5)
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDistributedCache>(distributed);
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = settings.DistributedCacheExpiration,
                LocalCacheExpiration = settings.LocalCacheExpiration
            };
        });

        await using var provider = services.BuildServiceProvider();
        var service = new HybridCacheService(
            provider.GetRequiredService<HybridCache>(),
            distributed,
            redis.Multiplexer,
            Options.Create(new RedisCacheOptions { InstanceName = "ECommerce:" }),
            Options.Create(settings),
            NullLogger<HybridCacheService>.Instance);

        var calls = 0;
        var value = await service.GetOrCreateAsync("products:failopen", _ =>
        {
            calls++;
            return Task.FromResult("from-db");
        }, TimeSpan.FromMinutes(5));

        value.Should().Be("from-db");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldFallbackToFactory_WhenGenerationReadFails()
    {
        await using var ctx = CreateContext();
        ctx.Redis.ThrowOnGenerationGet = true;
        var calls = 0;

        var value = await ctx.Service.GetOrCreateAsync("products:genfail", _ =>
        {
            calls++;
            return Task.FromResult("from-db");
        }, TimeSpan.FromMinutes(5));

        value.Should().Be("from-db");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task SetAsync_And_GetAsync_ShouldRoundTripSerializedValues()
    {
        await using var ctx = CreateContext();
        var payload = new Payload("name", 42);

        await ctx.Service.SetAsync("plain:payload", payload, TimeSpan.FromMinutes(5));

        (await ctx.Service.GetAsync<Payload>("plain:payload")).Should().BeEquivalentTo(payload);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnDefault_OnMissOrFailure()
    {
        await using var ctx = CreateContext();

        (await ctx.Service.GetAsync<string>("missing")).Should().BeNull();

        ctx.Distributed.ThrowOnGet = true;
        (await ctx.Service.GetAsync<string>("missing")).Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_And_RemoveByPrefixAsync_ShouldBeFailOpen_OnErrors()
    {
        await using var ctx = CreateContext();
        ctx.Distributed.ThrowOnSet = true;
        await ctx.Service.Invoking(s => s.SetAsync("products:x", "v")).Should().NotThrowAsync();

        ctx.Redis.ThrowOnIncrement = true;
        await ctx.Service.Invoking(s => s.RemoveByPrefixAsync(CacheScopes.Products)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldRespectCancellation()
    {
        await using var ctx = CreateContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => ctx.Service.GetOrCreateAsync("products:ct", _ => Task.FromResult("x"), ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldNotCacheFailedFactory()
    {
        await using var ctx = CreateContext();

        Func<Task> failing = () => ctx.Service.GetOrCreateAsync<string>("products:fail", _ =>
            Task.FromException<string>(new InvalidOperationException("boom")));
        await failing.Should().ThrowAsync<InvalidOperationException>();

        var calls = 0;
        (await ctx.Service.GetOrCreateAsync("products:fail", _ =>
        {
            calls++;
            return Task.FromResult("recovered");
        })).Should().Be("recovered");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldStoreSeparateEntries_ForDifferentPagingParameters()
    {
        await using var ctx = CreateContext();

        await ctx.Service.GetOrCreateAsync(
            ProductCacheKeys.List(new ProductSpecificationParameters { PageNumber = 1, PageSize = 10 }),
            _ => Task.FromResult("page 1"),
            TimeSpan.FromMinutes(5));
        await ctx.Service.GetOrCreateAsync(
            ProductCacheKeys.List(new ProductSpecificationParameters { PageNumber = 2, PageSize = 10 }),
            _ => Task.FromResult("page 2"),
            TimeSpan.FromMinutes(5));

        (await ctx.Service.GetOrCreateAsync(
            ProductCacheKeys.List(new ProductSpecificationParameters { PageNumber = 1, PageSize = 10 }),
            _ => Task.FromResult("wrong"),
            TimeSpan.FromMinutes(5))).Should().Be("page 1");
    }

    [Fact]
    public async Task CacheSettings_ShouldConfigureLocalAndDistributedExpirations()
    {
        var settings = new CacheSettings
        {
            DefaultExpiration = TimeSpan.FromMinutes(10),
            LocalCacheExpiration = TimeSpan.FromMinutes(1),
            DistributedCacheExpiration = TimeSpan.FromMinutes(8)
        };

        settings.EffectiveDistributed(null).Should().Be(TimeSpan.FromMinutes(8));
        settings.EffectiveDistributed(TimeSpan.FromMinutes(3)).Should().Be(TimeSpan.FromMinutes(3));
        settings.EffectiveLocal(TimeSpan.FromMinutes(5)).Should().Be(TimeSpan.FromMinutes(1));
        settings.EffectiveLocal(TimeSpan.FromSeconds(30)).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void CacheSettings_ShouldRejectLocalGreaterThanDistributed()
    {
        var settings = new CacheSettings
        {
            LocalCacheExpiration = TimeSpan.FromMinutes(10),
            DistributedCacheExpiration = TimeSpan.FromMinutes(5)
        };

        settings.Invoking(s => s.Validate()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldStoreDistinctL2Entries_PerGenerationScopedKey()
    {
        await using var ctx = CreateContext();

        await ctx.Service.GetOrCreateAsync("products:details", _ => Task.FromResult("v1"), TimeSpan.FromMinutes(5));
        await ctx.Service.RemoveByPrefixAsync(CacheScopes.Products);
        await ctx.Service.GetOrCreateAsync("products:details", _ => Task.FromResult("v2"), TimeSpan.FromMinutes(5));

        (await ctx.Service.GetOrCreateAsync("products:details", _ => Task.FromResult("v3"), TimeSpan.FromMinutes(5)))
            .Should().Be("v2");
    }

    private static TestCacheContext CreateContext(
        InMemoryDistributedCache? distributedCache = null,
        FakeRedisDistributed? redis = null,
        CacheSettings? settings = null)
    {
        distributedCache ??= new InMemoryDistributedCache();
        redis ??= new FakeRedisDistributed(distributedCache);
        settings ??= new CacheSettings
        {
            DefaultExpiration = TimeSpan.FromMinutes(5),
            LocalCacheExpiration = TimeSpan.FromMinutes(2),
            DistributedCacheExpiration = TimeSpan.FromMinutes(5)
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDistributedCache>(distributedCache);
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = settings.DistributedCacheExpiration,
                LocalCacheExpiration = settings.LocalCacheExpiration
            };
        });

        var provider = services.BuildServiceProvider();
        var hybridCache = provider.GetRequiredService<HybridCache>();

        var service = new HybridCacheService(
            hybridCache,
            distributedCache,
            redis.Multiplexer,
            Options.Create(new RedisCacheOptions { InstanceName = "ECommerce:" }),
            Options.Create(settings),
            NullLogger<HybridCacheService>.Instance);

        return new TestCacheContext(service, distributedCache, redis, provider);
    }

    private sealed record Payload(string Name, int Value);

    private sealed class TestCacheContext(
        ICacheService service,
        InMemoryDistributedCache distributed,
        FakeRedisDistributed redis,
        ServiceProvider provider) : IAsyncDisposable
    {
        public ICacheService Service { get; } = service;
        public InMemoryDistributedCache Distributed { get; } = distributed;
        public FakeRedisDistributed Redis { get; } = redis;

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    /// <summary>Simple in-memory IDistributedCache for tests (no TTL enforcement needed).</summary>
    private sealed class InMemoryDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);
        public bool ThrowOnGet { get; set; }
        public bool ThrowOnSet { get; set; }

        public byte[]? Get(string key) => GetAsync(key, default).GetAwaiter().GetResult();

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            if (ThrowOnGet)
            {
                throw new RedisException("simulated get failure");
            }

            return Task.FromResult(_entries.TryGetValue(key, out var value) ? value : null);
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            SetAsync(key, value, options).GetAwaiter().GetResult();

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            if (ThrowOnSet)
            {
                throw new RedisException("simulated set failure");
            }

            _entries[key] = value;
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            _entries.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public void Clear() => _entries.Clear();
    }

    private sealed class ThrowingDistributedCache(bool throwOnGet) : IDistributedCache
    {
        public byte[]? Get(string key) => throw new RedisException("boom");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throwOnGet ? throw new RedisException("boom") : Task.FromResult<byte[]?>(null);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }

    /// <summary>Fakes the Redis multiplexer/database used for generation counters.</summary>
    private sealed class FakeRedisDistributed
    {
        private readonly ConcurrentDictionary<string, long> _generations = new(StringComparer.OrdinalIgnoreCase);
        private readonly IConnectionMultiplexer _multiplexer;

        public bool ThrowOnGenerationGet { get; set; }
        public bool ThrowOnIncrement { get; set; }

        public FakeRedisDistributed(IDistributedCache _)
        {
            var database = Substitute.For<IDatabase>();
            database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
                .Returns(call =>
                {
                    if (ThrowOnGenerationGet)
                    {
                        throw new RedisException("generation get failed");
                    }

                    var key = call.Arg<RedisKey>().ToString();
                    var scope = ExtractScope(key);
                    return Task.FromResult((RedisValue)GetGeneration(scope));
                });

            database.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
                .Returns(call =>
                {
                    if (ThrowOnIncrement)
                    {
                        throw new RedisException("increment failed");
                    }

                    var key = call.Arg<RedisKey>().ToString();
                    var scope = ExtractScope(key);
                    var by = (long)call.ArgAt<long>(1);
                    return Task.FromResult(Increment(scope, by));
                });

            database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
                .Returns(_ => Task.FromResult(RedisResult.Create(RedisValue.Null)));

            _multiplexer = Substitute.For<IConnectionMultiplexer>();
            _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);
        }

        public IConnectionMultiplexer Multiplexer => _multiplexer;

        private long GetGeneration(string scope) => _generations.TryGetValue(scope, out var v) ? v : 0;
        private long Increment(string scope, long by) => _generations.AddOrUpdate(scope, by, (_, cur) => cur + by);

        private static string ExtractScope(string key)
        {
            const string marker = "__gen:";
            var idx = key.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? key[(idx + marker.Length)..] : key;
        }
    }
}
