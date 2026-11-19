# Caching, pagination, and validation

## Cache consistency

`MemoryCacheService` now keeps key and in-flight state on the service instance instead of in static fields. The application registers it as a singleton, so one instance owns one consistent view of its cache.

Three problems were fixed:

1. **Cache stampede.** `GetOrCreateAsync` uses a per-key `Lazy<Task<...>>`. Concurrent callers for the same key await one factory invocation.
2. **Stale writes.** Every invalidation or explicit write bumps a generation. A loader only writes its result if the generation and its flight reference are unchanged when the factory completes. A slow factory therefore cannot resurrect a key that was removed or overwritten while it ran.
3. **Prefix invalidation.** Matching tracked keys and active flights are removed together. Key normalization is case-insensitive to match the previous API behavior and to keep `Products_1` and `products_1` from becoming separate logical keys.

Caller cancellation stops that caller's wait; it does not cancel shared work for every other waiter. The shared factory receives `CancellationToken.None` for that reason. Host shutdown cancellation remains the responsibility of the factory's own dependencies if needed.

`null` results are cached as a `CacheEntry` wrapper, so a cached null is distinguishable from a cache miss inside `GetOrCreateAsync`.

## Product pagination

`GET /api/v1/Products` now returns:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [],
    "pageIndex": 1,
    "pageSize": 10,
    "totalCount": 0,
    "totalPages": 0,
    "hasPrevious": false,
    "hasNext": false
  }
}
```

`ProductsCountSpecification` and `ProductsWithBrandAndTypeSpecification` share the same filter expression, so `totalCount` always describes the same result set as `items`. Sorting now includes a deterministic `Id` tie-breaker; otherwise equal-price rows could move between pages.

Invalid input is rejected with `400 Validation ProblemDetails` instead of being silently clamped:

- `PageIndex` must be at least 1.
- `PageSize` must be between 1 and 50.
- `Search` is limited to 100 characters.
- `Sort` must be `nameasc`, `namedesc`, `priceasc`, or `pricedesc`.

Validation runs in the existing MediatR `ValidationBehavior`, and `GlobalExceptionMiddleware` renders `ValidationException` as `HttpValidationProblemDetails`, so query validation uses the same response shape as command validation.

## Application boundary for password reset

Password-reset link construction, HTML rendering, the email outbox payload, and the idempotency key now live in `ECommerce.UseCases`. `IdentityService` still owns ASP.NET Core Identity operations and persists the resulting outbox row.

This is a deliberate partial move rather than a rewrite. Moving all of `IdentityService` into UseCases would require abstracting `UserManager`, `SignInManager`, and the outbox DbContext for little benefit. The application-specific rules are now testable without Identity or SMTP.

## Why no broad value-object migration

`PagedResponse<T>` is the only new value object in this phase. It has real behavior (total pages and navigation flags) and no database mapping. Introducing `Money` or `EmailAddress` across `Product` and Identity would create mapping churn without changing a current invariant, so it was deferred.
