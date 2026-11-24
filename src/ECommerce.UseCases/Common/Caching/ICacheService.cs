namespace ECommerce.UseCases.Common.Caching;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);

    Task RemoveByPrefixAsync(string prefixKey, CancellationToken ct = default);

    Task<T> GetOrCreateAsync<T>(
        string key, 
        Func<CancellationToken, Task<T>> factory, 
        TimeSpan? expiration = null, 
        CancellationToken ct = default);
}
