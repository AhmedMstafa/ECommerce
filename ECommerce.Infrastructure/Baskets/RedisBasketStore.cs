using System.Globalization;
using System.Text.Json;
using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ECommerce.Infrastructure.Baskets;

/// <summary>
/// Redis document store for baskets. Uses compare-and-set Lua scripts so
/// read-modify-write operations never lose concurrent updates.
/// </summary>
public sealed class RedisBasketStore(
    IConnectionMultiplexer multiplexer,
    ILogger<RedisBasketStore> logger) : IBasketStore
{
    private const string SaveScript = """
        local current = redis.call('GET', KEYS[1])
        if not current then
            if ARGV[1] == '-1' then
                redis.call('SET', KEYS[1], ARGV[2], 'EX', ARGV[3])
                return { 'saved' }
            end
            return { 'conflict', '' }
        end

        local document = cjson.decode(current)
        local revision = document['Revision']
        if revision == nil then revision = document['revision'] end
        if revision == nil then revision = 0 end

        if tostring(revision) == ARGV[1] then
            redis.call('SET', KEYS[1], ARGV[2], 'EX', ARGV[3])
            return { 'saved' }
        end

        return { 'conflict', current }
        """;

    private const string DeleteScript = """
        local current = redis.call('GET', KEYS[1])
        if not current then
            return { 'deleted' }
        end

        local document = cjson.decode(current)
        local revision = document['Revision']
        if revision == nil then revision = document['revision'] end
        if revision == nil then revision = 0 end

        if tostring(revision) == ARGV[1] then
            redis.call('DEL', KEYS[1])
            return { 'deleted' }
        end

        return { 'conflict' }
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _database = multiplexer.GetDatabase();

    public async Task<Basket?> GetAsync(BasketKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var value = await _database.StringGetAsync(key.Value).WaitAsync(ct);
            if (!value.HasValue)
            {
                return null;
            }

            var document = JsonSerializer.Deserialize<BasketDocument>((byte[])value!, Json);
            return document?.ToEntity();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailure("get", key, ex);
            throw new BasketStorageException("Failed to read the basket from Redis.", ex);
        }
    }

    public async Task<BasketSaveResult> SaveAsync(
        Basket basket,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var json = JsonSerializer.Serialize(BasketDocument.FromEntity(basket), Json);
            var expectedRevision = basket.ExpectedRevision;
            var ttlSeconds = Math.Max(1, (long)Math.Ceiling(ttl.TotalSeconds));

            var result = await _database.ScriptEvaluateAsync(
                    SaveScript,
                    [new RedisKey(basketKey(basket).Value)],
                    [
                        expectedRevision.ToString(CultureInfo.InvariantCulture),
                        json,
                        ttlSeconds.ToString(CultureInfo.InvariantCulture)
                    ])
                .WaitAsync(ct);

            if (result.IsNull || result.Length == 0)
            {
                return new BasketSaveResult(BasketSaveOutcome.Failed, null);
            }

            var status = (string?)result[0];
            if (string.Equals(status, "saved", StringComparison.Ordinal))
            {
                basket.MarkPersisted();
                return new BasketSaveResult(BasketSaveOutcome.Saved, basket);
            }

            var currentJson = result.Length > 1 ? (string?)result[1] : null;
            var current = string.IsNullOrEmpty(currentJson)
                ? null
                : JsonSerializer.Deserialize<BasketDocument>(currentJson, Json)?.ToEntity();

            return new BasketSaveResult(BasketSaveOutcome.Conflict, current);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailure("save", basketKey(basket), ex);
            return new BasketSaveResult(BasketSaveOutcome.Failed, null);
        }
    }

    public async Task<bool> DeleteAsync(BasketKey key, long expectedRevision, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var result = await _database.ScriptEvaluateAsync(
                    DeleteScript,
                    [new RedisKey(key.Value)],
                    [expectedRevision.ToString(CultureInfo.InvariantCulture)])
                .WaitAsync(ct);

            if (result.IsNull || result.Length == 0)
            {
                return false;
            }

            return string.Equals((string?)result[0], "deleted", StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Merge already persisted the user basket; the idempotency marker
            // protects retries, so a failed guest delete must not roll back.
            LogFailure("delete", key, ex);
            return false;
        }
    }

    private static BasketKey basketKey(Basket basket) =>
        basket.UserId is { } userId
            ? BasketKey.ForUser(userId)
            : BasketKey.ForGuest(basket.GuestTokenHash
                ?? throw new InvalidOperationException("A guest basket must have a token hash."));

    private void LogFailure(string operation, BasketKey key, Exception ex)
    {
        logger.LogWarning(
            "Basket Redis {Operation} failed for {BasketKind} ({ErrorType}).",
            operation,
            key.Kind,
            ex.GetType().Name);
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
            Items = basket.Items
                .Select(item => new BasketItemDocument
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    PriceAtAdd = item.PriceAtAdd,
                    AddedAt = item.AddedAt
                })
                .ToList(),
            MergedGuestTokenHashes = basket.MergedGuestTokenHashes.ToList()
        };

        public Basket ToEntity() => Basket.Rehydrate(
            Id == Guid.Empty ? Guid.NewGuid() : Id,
            UserId,
            GuestTokenHash,
            CreatedAt == default ? DateTime.UtcNow : CreatedAt,
            UpdatedAt == default ? DateTime.UtcNow : UpdatedAt,
            Revision,
            Items.Select(item => BasketItem.Rehydrate(
                item.ProductId,
                item.Quantity,
                item.PriceAtAdd,
                item.AddedAt == default ? DateTime.UtcNow : item.AddedAt)),
            MergedGuestTokenHashes.Where(hash => !string.IsNullOrWhiteSpace(hash)));
    }

    private sealed record BasketItemDocument
    {
        public Guid ProductId { get; init; }

        public int Quantity { get; init; }

        public decimal? PriceAtAdd { get; init; }

        public DateTime AddedAt { get; init; }
    }
}
