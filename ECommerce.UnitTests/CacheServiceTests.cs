using ECommerce.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;

namespace ECommerce.UnitTests;

public class CacheServiceTests
{
    [Fact]
    public async Task GetOrCreateAsync_ShouldLoadOnceUnderConcurrency()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new MemoryCacheService(memoryCache);
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
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldNotCacheStaleResultAfterRemoveByPrefix()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new MemoryCacheService(memoryCache);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var load = service.GetOrCreateAsync("products:1", async _ =>
        {
            started.SetResult();
            await release.Task;
            return "stale";
        });
        await started.Task;

        await service.RemoveByPrefixAsync("products:");
        release.SetResult();
        await load;

        (await service.GetAsync<string>("products:1")).Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldNotOverwriteNewerExplicitValue()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new MemoryCacheService(memoryCache);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var load = service.GetOrCreateAsync("key", async _ =>
        {
            started.SetResult();
            await release.Task;
            return "stale";
        });
        await started.Task;

        await service.SetAsync("key", "fresh");
        release.SetResult();
        await load;

        (await service.GetAsync<string>("key")).Should().Be("fresh");
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldNotCacheFailedFactory()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new MemoryCacheService(memoryCache);
        var calls = 0;

        Task<string> First(CancellationToken _) => Task.FromException<string>(new InvalidOperationException("boom"));

        await service.Invoking(s => s.GetOrCreateAsync("key", First))
            .Should().ThrowAsync<InvalidOperationException>();

        var second = await service.GetOrCreateAsync("key", _ =>
        {
            calls++;
            return Task.FromResult("recovered");
        });

        second.Should().Be("recovered");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldCacheNullReferenceResults()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new MemoryCacheService(memoryCache);

        var result = await service.GetOrCreateAsync<string?>("key", _ => Task.FromResult<string?>(null));
        result.Should().BeNull();

        var extraCalls = 0;
        await service.GetOrCreateAsync<string?>("key", _ =>
        {
            extraCalls++;
            return Task.FromResult<string?>("not used");
        });

        extraCalls.Should().Be(0);
    }

    [Fact]
    public async Task RemoveByPrefixAsync_ShouldUseInstanceScopedTracking()
    {
        using var sharedCache = new MemoryCache(new MemoryCacheOptions());
        var firstService = new MemoryCacheService(sharedCache);
        var secondService = new MemoryCacheService(sharedCache);

        await secondService.SetAsync("products:1", "one");
        await firstService.RemoveByPrefixAsync("products:");

        (await secondService.GetAsync<string>("products:1")).Should().Be("one");
    }

    [Fact]
    public async Task RemoveByPrefixAsync_ShouldRemoveOnlyMatchingKeys()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new MemoryCacheService(memoryCache);

        await service.SetAsync("products:1", "one");
        await service.SetAsync("products:2", "two");
        await service.SetAsync("brands:all", "brands");

        await service.RemoveByPrefixAsync("products:");

        (await service.GetAsync<string>("products:1")).Should().BeNull();
        (await service.GetAsync<string>("products:2")).Should().BeNull();
        (await service.GetAsync<string>("brands:all")).Should().Be("brands");
    }

    private static async Task<T> WaitForAsync<T>(Task gate, T value)
    {
        await gate;
        return value;
    }
}
