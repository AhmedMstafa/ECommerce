namespace ECommerce.UseCases.Common.Caching;

/// <summary>
/// Logical cache scopes used for invalidation. A scope maps to one generation
/// counter in Redis, so every key whose first segment matches the scope is
/// invalidated together when writes occur.
/// </summary>
public static class CacheScopes
{
    public const string Products = "products";
}
