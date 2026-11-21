using System.Text.Json;
using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Common.Caching;
using ECommerce.UseCases.Common.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace ECommerce.IntegrationTests;

/// <summary>
/// Boots the real API against the throwaway SQL Server database. External
/// side effects (SMTP, Google certificate HTTP calls, Hangfire workers) are
/// removed so tests exercise application and persistence code, not the network.
/// </summary>
public sealed class TestingWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _originalEnvironment;

    public TestingWebApplicationFactory(string connectionString)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings__DefaultConnection"] = connectionString,
            ["Jwt__SecretKey"] = $"integration-tests-only-{Guid.NewGuid():N}",
            ["Swagger__Enabled"] = "true",
            ["ClientApp__BaseUrl"] = "https://client.example.com",
            ["RateLimiting__Fixed__PermitLimit"] = "10000",
            ["RateLimiting__Auth__PermitLimit"] = "10000",
            // AddInfrastructure validates Redis settings at startup. A dummy,
            // parseable value is enough because the cache is replaced below.
            ["Redis__Configuration"] = "localhost:6379,abortConnect=false",
            ["Redis__InstanceName"] = "ECommerceTest:"
        };

        // Program.cs captures several settings into locals while registering
        // services, before WebApplicationFactory's ConfigureAppConfiguration
        // callbacks can affect IConfiguration. Environment variables are loaded
        // by WebApplication.CreateBuilder itself, so they override those locals.
        _originalEnvironment = settings.Keys.ToDictionary(
            key => key,
            Environment.GetEnvironmentVariable);

        foreach (var (key, value) in settings)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService, RecordingEmailService>();

            // Replace the Redis-backed cache with a pass-through so integration
            // tests never open a network connection or share cached state.
            services.RemoveAll<ICacheService>();
            services.RemoveAll<IDistributedCache>();
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton<ICacheService, PassThroughCacheService>();

            // Baskets are stored in Redis in production; tests use an in-memory
            // store so the suite runs without a Redis dependency.
            services.RemoveAll<IBasketStore>();
            services.AddSingleton<IBasketStore, InMemoryBasketStore>();

            services.RemoveAll<IGoogleTokenValidator>();
            services.AddSingleton<IGoogleTokenValidator, StubGoogleTokenValidator>();

            // AddInfrastructure registers a Hangfire BackgroundJobServerHostedService.
            // Tests must not have a background worker touching the test database.
            var hangfireHostedServices = services
                .Where(descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                                     IsHangfireHostedService(descriptor))
                .ToList();

            foreach (var descriptor in hangfireHostedServices)
            {
                services.Remove(descriptor);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var (key, value) in _originalEnvironment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        base.Dispose(disposing);
    }

    private static bool IsHangfireHostedService(ServiceDescriptor descriptor)
    {
        var implementationName = descriptor.ImplementationType?.FullName
                                 ?? descriptor.ImplementationInstance?.GetType().FullName;

        if (implementationName is not null)
        {
            return implementationName.StartsWith("Hangfire.", StringComparison.Ordinal);
        }

        return descriptor.ImplementationFactory?.Method.ReturnType.FullName?
                   .StartsWith("Hangfire.", StringComparison.Ordinal) == true;
    }
}

/// <summary>
/// Records direct send attempts. Password reset emails should be queued in the
/// outbox, not sent from the request pipeline, so direct sends stay at zero.
/// </summary>
public sealed class RecordingEmailService : IEmailService
{
    private int _sendCount;

    public int SendCount => Volatile.Read(ref _sendCount);

    public Task<Result> SendEmailAsync(
        string to,
        string subject,
        string body,
        bool isHtml = true,
        CancellationToken ct = default,
        string? messageId = null)
    {
        Interlocked.Increment(ref _sendCount);
        return Task.FromResult(Result.Success());
    }
}

/// <summary>
/// Deterministic Google identity stub. Distinct tokens cover successful login,
/// explicit-link mismatch, and an unlinked email owner without network calls.
/// </summary>
public sealed class StubGoogleTokenValidator : IGoogleTokenValidator
{
    public const string NewUserToken = "google-new-user-token";
    public const string ExistingEmailToken = "google-existing-email-token";
    public const string InvalidToken = "google-invalid-token";

    public const string NewUserEmail = "google-new-user@example.com";
    public const string NewUserSubject = "google-subject-new-user";

    public Task<Result<GoogleIdentity>> ValidateAsync(
        string idToken,
        CancellationToken ct = default)
    {
        var identity = idToken switch
        {
            NewUserToken => new GoogleIdentity(
                NewUserSubject,
                NewUserEmail,
                EmailVerified: true,
                FirstName: "Google",
                LastName: "User",
                Picture: null),
            ExistingEmailToken => new GoogleIdentity(
                "google-subject-existing-email",
                IntegrationTestFixture.AdminEmail,
                EmailVerified: true,
                FirstName: "System",
                LastName: "Administrator",
                Picture: null),
            _ => null
        };

        return Task.FromResult(identity is null
            ? Result.Failure<GoogleIdentity>(Error.Validation(
                "Google.InvalidToken",
                "The Google ID token is invalid."))
            : Result.Success(identity));
    }
}

/// <summary>
/// Test replacement for ICacheService. Every read goes straight to the factory,
/// which keeps integration tests deterministic and free of Redis network I/O.
/// </summary>
public sealed class PassThroughCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
        Task.FromResult<T?>(default);

    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task RemoveByPrefixAsync(string prefixKey, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken ct = default) => factory(ct);
}

/// <summary>
/// In-memory basket store for integration tests. Preserves optimistic
/// concurrency and expiration semantics without requiring Redis.
/// </summary>
public sealed class InMemoryBasketStore : IBasketStore
{
    private sealed record Entry(string Json, DateTimeOffset? ExpiresAt);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<Basket?> GetAsync(BasketKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_entries.TryGetValue(key.Value, out var entry))
            {
                return Task.FromResult<Basket?>(null);
            }

            if (entry.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            {
                _entries.Remove(key.Value);
                return Task.FromResult<Basket?>(null);
            }

            var document = JsonSerializer.Deserialize<BasketDocument>(entry.Json, Json);
            return Task.FromResult(document?.ToEntity());
        }
    }

    public Task<BasketSaveResult> SaveAsync(Basket basket, TimeSpan ttl, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var key = (basket.UserId is { } userId
                ? BasketKey.ForUser(userId)
                : BasketKey.ForGuest(basket.GuestTokenHash!)).Value;

            var expectedRevision = basket.ExpectedRevision;
            Basket? current = null;

            if (_entries.TryGetValue(key, out var existing))
            {
                if (existing.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
                {
                    _entries.Remove(key);
                }
                else
                {
                    current = JsonSerializer.Deserialize<BasketDocument>(existing.Json, Json)?.ToEntity();
                }
            }

            var currentRevision = current?.Revision ?? -1;
            if (currentRevision != expectedRevision)
            {
                return Task.FromResult(new BasketSaveResult(BasketSaveOutcome.Conflict, current));
            }

            basket.MarkPersisted();
            var json = JsonSerializer.Serialize(BasketDocument.FromEntity(basket), Json);
            var newExpiresAt = DateTimeOffset.UtcNow.Add(ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : ttl);
            _entries[key] = new Entry(json, newExpiresAt);
            return Task.FromResult(new BasketSaveResult(BasketSaveOutcome.Saved, basket));
        }
    }

    public Task<bool> DeleteAsync(BasketKey key, long expectedRevision, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_entries.TryGetValue(key.Value, out var existing))
            {
                return Task.FromResult(true);
            }

            if (existing.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            {
                _entries.Remove(key.Value);
                return Task.FromResult(true);
            }

            var current = JsonSerializer.Deserialize<BasketDocument>(existing.Json, Json)?.ToEntity();
            if (current is not null && current.Revision != expectedRevision)
            {
                return Task.FromResult(false);
            }

            _entries.Remove(key.Value);
            return Task.FromResult(true);
        }
    }

    private sealed record BasketDocument
    {
        public Guid Id { get; init; }
        public Guid? UserId { get; init; }
        public string? GuestTokenHash { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public long Revision { get; init; }
        public List<BasketItemDocument> Items { get; init; } = [];
        public List<string> MergedGuestTokenHashes { get; init; } = [];

        public static BasketDocument FromEntity(Basket basket) => new()
        {
            Id = basket.Id,
            UserId = basket.UserId,
            GuestTokenHash = basket.GuestTokenHash,
            CreatedAt = basket.CreatedAt,
            UpdatedAt = basket.UpdatedAt,
            Revision = basket.Revision,
            Items = basket.Items.Select(item => new BasketItemDocument
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                PriceAtAdd = item.PriceAtAdd,
                AddedAt = item.AddedAt
            }).ToList(),
            MergedGuestTokenHashes = basket.MergedGuestTokenHashes.ToList()
        };

        public Basket ToEntity() => Basket.Rehydrate(
            Id,
            UserId,
            GuestTokenHash,
            CreatedAt,
            UpdatedAt,
            Revision,
            Items.Select(item => BasketItem.Rehydrate(item.ProductId, item.Quantity, item.PriceAtAdd, item.AddedAt)),
            MergedGuestTokenHashes);
    }

    private sealed record BasketItemDocument
    {
        public Guid ProductId { get; init; }
        public int Quantity { get; init; }
        public decimal? PriceAtAdd { get; init; }
        public DateTime AddedAt { get; init; }
    }
}
