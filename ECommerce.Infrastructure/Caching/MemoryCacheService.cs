using System.Collections.Concurrent;
using ECommerce.UseCases.Common.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace ECommerce.Infrastructure.Caching;

public class MemoryCacheService(IMemoryCache memoryCache) : ICacheService
{
    private static readonly ConcurrentDictionary<string, byte> Keys = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        memoryCache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromMinutes(10)
        };

        options.RegisterPostEvictionCallback((k, _, _, _) => Keys.TryRemove(k.ToString()!, out _));

        memoryCache.Set(key, value, options);
        Keys.TryAdd(key, 0);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        memoryCache.Remove(key);
        Keys.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefixKey, CancellationToken ct = default)
    {
        var matchingKeys = Keys.Keys.Where(k => k.StartsWith(prefixKey, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var key in matchingKeys)
        {
            memoryCache.Remove(key);
            Keys.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key, 
        Func<CancellationToken, Task<T>> factory, 
        TimeSpan? expiration = null, 
        CancellationToken ct = default)
    {
        if (memoryCache.TryGetValue(key, out T? cachedValue) && cachedValue is not null)
        {
            return cachedValue;
        }

        var result = await factory(ct);
        await SetAsync(key, result, expiration, ct);
        return result;
    }
}
