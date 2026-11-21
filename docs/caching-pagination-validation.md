# Caching, pagination, and validation

## Redis cache (Upstash)

`IMemoryCache`, `AddMemoryCache()`, and `MemoryCacheService` have been removed.
All application caching now flows through the existing `ICacheService`
abstraction, implemented by `RedisCacheService` in `ECommerce.Infrastructure`:

```text
Product/Brand/Type queries ──► ICacheService ──► RedisCacheService
                                                   ├─ IDistributedCache
                                                   │  (AddStackExchangeRedisCache)
                                                   └─ IConnectionMultiplexer
                                                      (atomic generation counters)
```

`AddStackExchangeRedisCache` is registered in `AddInfrastructure` with a single
`ConfigurationOptions` object. A single `IConnectionMultiplexer` is registered as
a singleton and is also supplied to `RedisCacheOptions.ConnectionMultiplexerFactory`,
so the generation counter, the cache service and the framework `RedisCache` share
one connection and one client pool. `IDistributedCache` still owns the physical
data keys, TTL metadata and writes; the atomic read script reads the same hash
layout (`HGET … data`) that the framework writes.

### Create an Upstash database

1. Sign in at <https://console.upstash.com/> and choose **Create Database**.
2. Select a region close to the API, enable **TLS**, and keep the default port
   `6379`.
3. Copy the connection string shown under **Connect → .NET** (or the `rediss://`
   URL in the REST/CLI tab). Treat it as a password: it contains credentials.
4. Put it in the repository root `.env` as `Redis__Configuration` (see below).
   Never commit `.env` and never paste the value into issues, logs, or reports.

### Local development configuration

There is no User Secrets setup for Redis in this repository. The root `.env` is
the single local source, loaded by `Program.cs` through `DotNetEnv` before the
host is built:

```dotenv
Redis__Configuration=rediss://default:PASSWORD@HOST:6379
Redis__InstanceName=ECommerce:
```

`rediss://` enables TLS. The plain `redis://` scheme and the classic
`host:port,password=...,ssl=True` form are also accepted. Without a scheme the
connection defaults to port `6379`, and `AbortOnConnectFail=false` is always set
so a temporary outage cannot stop the API from starting.

### Production configuration

Inject `Redis__Configuration` (and optionally `Redis__InstanceName`) as
environment variables from your secret store. Configuration can change without
a rebuild. Startup validation checks that a value exists and is not a
placeholder; the error names the key but never prints the value.

### Cache keys and TTL

Logical keys are built in `ProductCacheKeys` and include every parameter that
changes the result: page number, page size, search, brand, type, min/max price,
resolved sort field, resolved direction, and both legacy and new sort inputs.
Tokens are trimmed, lowercased, and URL-escaped to avoid collisions.

```text
ECommerce:Products:Page:1:Size:10:Search:laptop:Brand:…:Type:…:MinPrice:…:MaxPrice:…:Sort:price:Order:asc:LegacySort:…:SortBy:…:SortOrder:…|g0
ECommerce:Products:Id:99b1…|g0
```

`ECommerce:` is the `InstanceName` applied by `IDistributedCache`; `|g0` is the
generation suffix applied by `RedisCacheService`. Expiration uses absolute Redis
TTLs so a hot key cannot stay stale forever: 2 minutes for product listings,
5 minutes for product details, 30 minutes for brands/types, and a 10-minute
fallback when a caller does not specify one.

### Invalidation

Product writes call `RemoveByPrefixAsync(CacheScopes.Products)`, which performs a
single atomic `INCR` on `ECommerce:gen:products`. Every product key is then read
under the new generation, so all page/filter/sort/search combinations and all
product-detail entries become unreachable at once. No `KEYS` or `SCAN` is used.
Old entries remain in Redis until their TTL expires; this is the trade-off for
O(1), race-safe invalidation. Each scope has its own counter, so brand/type
caches are unaffected.

The hit path uses one Lua `EVAL` round trip. The script reads the generation,
validates that it contains only digits, appends it to the instance-name-prefixed
data key, and reads the `data` hash field in the same Redis operation. A missing
counter is treated as generation `0`. Because Redis executes scripts atomically,
an `INCR` cannot interleave between the generation read and the value read; the
result is linearizable at the moment the script runs. An invalidation that
completes afterwards is seen by the next read. The post-lock check on a miss uses
the same atomic read, so it still detects writes or invalidations that happen
while callers queue.

`CreateProductCommand` already uses this path; any future update/delete command
should call the same method with `CacheScopes.Products`.

### Failure handling and logging

Redis is treated as an optimization. Read, write, remove, and generation errors
are caught, logged as structured warnings with `Operation`, `CacheKey`, and
`ErrorType` only, then degraded to a cache miss. The SQL query still runs and a
cache outage does not fail an otherwise valid request. No connection string,
password, stack trace, or internal exception message is exposed to clients or
written to logs.

Concurrent callers for the same key and generation share one process-local
factory execution (single flight). Caller cancellation stops that caller's wait
but does not cancel shared work needed by the other waiters; the factory and
shared write intentionally receive `CancellationToken.None`. Per-request cache
reads, writes, removals, invalidations and gate waits observe the caller's
`CancellationToken`. Cached `null` results are wrapped in `CacheEnvelope<T>` so
they are distinct from a miss.

### Verify connectivity and inspect keys

Do not paste the connection string into shell history. Prefer the Upstash
Console (**Data** tab to browse keys and TTL; **CLI** tab to run commands) or a
secret-aware shell variable:

```powershell
# PING without echoing the URL; TLS is implied by the rediss:// scheme.
redis-cli -u $env:REDIS__CONFIGURATION PING

# Inspect one key and its remaining TTL (keys are prefixed with ECommerce:).
redis-cli -u $env:REDIS__CONFIGURATION TTL 'ECommerce:Products:Id:<guid>|g0'
```

Run these with the real value loaded in the process environment. The API health
probes deliberately do **not** depend on Redis, because Redis is optional at
runtime; a degraded cache still serves requests from SQL Server.

### Testing

Unit tests cover Redis reads/writes, TTL propagation, hit/miss behaviour, key
generation, generation invalidation, different pagination keys, cancellation,
single-flight cancellation, one-round-trip atomic reads, concurrent invalidation,
fallback when `IDistributedCache`, the script or the generation counter fails,
and singleton connection reuse. They use a fake `IDistributedCache` and a
substituted `IConnectionMultiplexer`, so no Upstash account is needed. Integration
tests replace `ICacheService` with a pass-through implementation and inject a
dummy Redis configuration, so they never open a network connection.

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
