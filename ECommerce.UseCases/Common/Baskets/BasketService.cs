using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Baskets.Dtos;
using ECommerce.UseCases.Products.Specifications;
using Microsoft.Extensions.Options;

namespace ECommerce.UseCases.Common.Baskets;

public sealed class BasketService(
    IBasketStore store,
    IReadRepository<Product> productRepository,
    IOptions<BasketSettings> options,
    TimeProvider clock) : IBasketService
{
    private const int MaximumSaveAttempts = 3;

    private const string StatusAvailable = "Available";
    private const string StatusOutOfStock = "OutOfStock";
    private const string StatusUnavailable = "Unavailable";
    private const string StatusQuantityReduced = "QuantityReduced";

    private readonly BasketSettings _settings = options.Value;

    public async Task<Result<BasketResponse>> GetAsync(BasketKey key, CancellationToken ct = default)
    {
        try
        {
            var basket = await LoadAsync(key, ct) ?? CreateBasket(key, Now);
            return Result.Success(await BuildResponseAsync(basket, ct));
        }
        catch (BasketStorageException)
        {
            return StorageFailure();
        }
    }

    public async Task<Result<BasketResponse>> AddItemAsync(
        BasketKey key,
        Guid productId,
        int quantity,
        CancellationToken ct = default)
    {
        try
        {
            var invalid = ValidateQuantity(quantity);
            if (invalid is not null)
            {
                return Result.Failure<BasketResponse>(invalid);
            }

            return await MutateAsync(
                key,
                async (basket, now, token) =>
                {
                    var existing = FindItem(basket, productId);
                    if (existing is null && basket.Items.Count >= _settings.MaximumItems)
                    {
                        return BasketErrors.MaximumItemsReached(_settings.MaximumItems);
                    }

                    var product = await productRepository.GetByIdAsync(productId, token);
                    if (product is null)
                    {
                        return BasketErrors.ProductUnavailable(productId);
                    }

                    var limit = Math.Min(_settings.MaximumQuantityPerProduct, product.StockQuantity);
                    if ((existing?.Quantity ?? 0) + quantity > limit)
                    {
                        return QuantityFailure(product, productId, _settings.MaximumQuantityPerProduct);
                    }

                    basket.AddOrIncrease(productId, quantity, product.Price, now);
                    return MutationOutcome.Changed;
                },
                ct);
        }
        catch (BasketStorageException)
        {
            return StorageFailure();
        }
    }

    public async Task<Result<BasketResponse>> RemoveItemAsync(
        BasketKey key,
        Guid productId,
        CancellationToken ct = default)
    {
        try
        {
            return await MutateAsync(
                key,
                (basket, now, _) => Task.FromResult<Result<MutationOutcome>>(
                    basket.Remove(productId, now)
                        ? MutationOutcome.Changed
                        : MutationOutcome.Unchanged),
                ct);
        }
        catch (BasketStorageException)
        {
            return StorageFailure();
        }
    }

    public async Task<Result<BasketResponse>> UpdateQuantityAsync(
        BasketKey key,
        Guid productId,
        int quantity,
        CancellationToken ct = default)
    {
        try
        {
            var invalid = ValidateQuantity(quantity);
            if (invalid is not null)
            {
                return Result.Failure<BasketResponse>(invalid);
            }

            return await MutateAsync(
                key,
                async (basket, now, token) =>
                {
                    if (FindItem(basket, productId) is null)
                    {
                        return BasketErrors.ItemNotFound(productId);
                    }

                    var product = await productRepository.GetByIdAsync(productId, token);
                    if (product is null)
                    {
                        return BasketErrors.ProductUnavailable(productId);
                    }

                    var limit = Math.Min(_settings.MaximumQuantityPerProduct, product.StockQuantity);
                    if (quantity > limit)
                    {
                        return QuantityFailure(product, productId, _settings.MaximumQuantityPerProduct);
                    }

                    basket.SetQuantity(productId, quantity, product.Price, now);
                    return MutationOutcome.Changed;
                },
                ct);
        }
        catch (BasketStorageException)
        {
            return StorageFailure();
        }
    }

    public async Task<Result<BasketResponse>> ClearAsync(BasketKey key, CancellationToken ct = default)
    {
        try
        {
            return await MutateAsync(
                key,
                (basket, now, _) =>
                {
                    if (basket.Items.Count == 0)
                    {
                        return Task.FromResult<Result<MutationOutcome>>(MutationOutcome.Unchanged);
                    }

                    basket.Clear(now);
                    return Task.FromResult<Result<MutationOutcome>>(MutationOutcome.Changed);
                },
                ct);
        }
        catch (BasketStorageException)
        {
            return StorageFailure();
        }
    }

    public async Task<Result<BasketResponse>> MergeAsync(
        BasketKey userKey,
        string guestToken,
        CancellationToken ct = default)
    {
        try
        {
            if (userKey.Kind != BasketKind.User)
            {
                return Result.Failure<BasketResponse>(BasketErrors.AuthenticationRequired);
            }

            if (!GuestToken.IsValid(guestToken))
            {
                return Result.Failure<BasketResponse>(BasketErrors.GuestTokenRequired);
            }

            var guestTokenHash = GuestToken.Hash(guestToken);
            var now = Now;
            var userBasket = await LoadAsync(userKey, ct) ?? CreateBasket(userKey, now);

            // A retry after a successful merge must never add the guest items twice.
            if (userBasket.HasMergedGuest(guestTokenHash))
            {
                return Result.Success(await BuildResponseAsync(userBasket, ct));
            }

            var guestKey = BasketKey.ForGuest(guestTokenHash);
            var guestBasket = await LoadAsync(guestKey, ct);
            if (guestBasket is null || guestBasket.Items.Count == 0)
            {
                // Preserve the authenticated basket exactly as it is.
                return Result.Success(await BuildResponseAsync(userBasket, ct));
            }

            var productIds = guestBasket.Items.Select(item => item.ProductId).Distinct().ToArray();
            var products = await productRepository.GetAllWithSpecAsync(
                new ProductsByIdsSpecification(productIds),
                ct);
            var productById = products.ToDictionary(product => product.Id);

            var changed = false;
            foreach (var item in guestBasket.Items)
            {
                if (!productById.TryGetValue(item.ProductId, out var product))
                {
                    // Deleted or otherwise unavailable products are not migrated.
                    continue;
                }

                var limit = Math.Min(_settings.MaximumQuantityPerProduct, product.StockQuantity);
                var userQuantity = FindItem(userBasket, item.ProductId)?.Quantity ?? 0;
                var room = limit - userQuantity;
                if (room <= 0)
                {
                    continue;
                }

                var quantity = Math.Min(item.Quantity, room);
                userBasket.AddOrIncrease(item.ProductId, quantity, product.Price, now);
                changed = true;
            }

            if (!changed)
            {
                return Result.Success(await BuildResponseAsync(userBasket, ct));
            }

            userBasket.MarkGuestMerged(guestTokenHash, now);
            var save = await store.SaveAsync(
                userBasket,
                _settings.SlidingExpiration(BasketKind.User),
                ct);

            if (save.Outcome == BasketSaveOutcome.Failed)
            {
                return StorageFailure();
            }

            if (save.Outcome == BasketSaveOutcome.Conflict)
            {
                return Result.Failure<BasketResponse>(BasketErrors.ConcurrentUpdate);
            }

            // The guest basket is removed only after the user basket was saved.
            // If this delete fails, the marker keeps a retry idempotent.
            await store.DeleteAsync(guestKey, guestBasket.Revision, ct);

            return Result.Success(await BuildResponseAsync(userBasket, ct));
        }
        catch (BasketStorageException)
        {
            return StorageFailure();
        }
    }

    private async Task<Result<BasketResponse>> MutateAsync(
        BasketKey key,
        Func<Basket, DateTime, CancellationToken, Task<Result<MutationOutcome>>> mutate,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaximumSaveAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var now = Now;
            var basket = await LoadAsync(key, ct) ?? CreateBasket(key, now);
            var outcome = await mutate(basket, now, ct);

            if (outcome.IsFailure)
            {
                return Result.Failure<BasketResponse>(outcome.Error!);
            }

            if (outcome.Value == MutationOutcome.Unchanged)
            {
                return Result.Success(await BuildResponseAsync(basket, ct));
            }

            var save = await store.SaveAsync(basket, _settings.SlidingExpiration(key.Kind), ct);
            if (save.Outcome == BasketSaveOutcome.Saved)
            {
                return Result.Success(await BuildResponseAsync(basket, ct));
            }

            if (save.Outcome == BasketSaveOutcome.Failed)
            {
                return StorageFailure();
            }
        }

        return Result.Failure<BasketResponse>(BasketErrors.ConcurrentUpdate);
    }

    private async Task<Basket?> LoadAsync(BasketKey key, CancellationToken ct)
    {
        var basket = await store.GetAsync(key, ct);
        if (basket is null || Now - basket.CreatedAt < _settings.MaximumLifetime)
        {
            return basket;
        }

        // Absolute lifetime reached: start from a fresh basket instead of
        // keeping old state alive through sliding activity.
        return null;
    }

    private Basket CreateBasket(BasketKey key, DateTime now) => key.Kind == BasketKind.User
        ? Basket.CreateUser(Guid.Parse(key.Identifier), now)
        : Basket.CreateGuest(key.Identifier, now);

    private async Task<BasketResponse> BuildResponseAsync(Basket basket, CancellationToken ct)
    {
        var productIds = basket.Items.Select(item => item.ProductId).Distinct().ToArray();
        var products = productIds.Length == 0
            ? []
            : await productRepository.GetAllWithSpecAsync(
                new ProductsByIdsSpecification(productIds),
                ct);

        var productById = products.ToDictionary(product => product.Id);
        var items = new List<BasketItemResponse>(basket.Items.Count);
        var subtotal = 0m;
        var hasUnavailable = false;
        var hasPriceChanges = false;

        foreach (var item in basket.Items)
        {
            if (!productById.TryGetValue(item.ProductId, out var product))
            {
                hasUnavailable = true;
                items.Add(BuildItem(item, null, StatusUnavailable));
                continue;
            }

            if (product.StockQuantity <= 0)
            {
                hasUnavailable = true;
                items.Add(BuildItem(item, product, StatusOutOfStock));
                continue;
            }

            var status = item.Quantity > product.StockQuantity
                ? StatusQuantityReduced
                : StatusAvailable;
            var purchasableQuantity = Math.Min(item.Quantity, product.StockQuantity);
            var priced = BuildItem(item, product, status, purchasableQuantity);
            subtotal += priced.LineTotal;
            hasPriceChanges |= priced.HasPriceChanged;
            items.Add(priced);
        }

        return new BasketResponse
        {
            Id = basket.Id,
            UserId = basket.UserId,
            CreatedAt = basket.CreatedAt,
            UpdatedAt = basket.UpdatedAt,
            Items = items,
            TotalItems = basket.Items.Sum(item => item.Quantity),
            Subtotal = subtotal,
            HasUnavailableItems = hasUnavailable,
            HasPriceChanges = hasPriceChanges
        };
    }

    private static BasketItemResponse BuildItem(
        BasketItem item,
        Product? product,
        string status,
        int? purchasableQuantity = null)
    {
        var currentPrice = product?.Price ?? 0m;
        var hasPriceChanged = product is not null &&
                              item.PriceAtAdd is { } priceAtAdd &&
                              priceAtAdd != currentPrice;
        var priceDifference = item.PriceAtAdd is { } snapshot && product is not null
            ? currentPrice - snapshot
            : 0m;
        var lineTotal = product is null || status == StatusOutOfStock
            ? 0m
            : currentPrice * (purchasableQuantity ?? item.Quantity);

        return new BasketItemResponse
        {
            ProductId = item.ProductId,
            ProductName = product?.Name ?? string.Empty,
            PictureUrl = product?.PictureUrl ?? string.Empty,
            Quantity = item.Quantity,
            PriceAtAdd = item.PriceAtAdd,
            CurrentUnitPrice = currentPrice,
            HasPriceChanged = hasPriceChanged,
            PriceDifference = priceDifference,
            LineTotal = lineTotal,
            AvailableQuantity = product?.StockQuantity ?? 0,
            Status = status,
            CanCheckout = status is StatusAvailable or StatusQuantityReduced
        };
    }

    private Error? ValidateQuantity(int quantity) =>
        quantity <= 0
            ? BasketErrors.InvalidQuantity
            : quantity > _settings.MaximumQuantityPerProduct
                ? BasketErrors.MaximumQuantityExceeded(_settings.MaximumQuantityPerProduct)
                : null;

    private static Error QuantityFailure(Product product, Guid productId, int configuredLimit) =>
        product.StockQuantity < configuredLimit
            ? BasketErrors.InsufficientStock(productId, product.StockQuantity)
            : BasketErrors.MaximumQuantityExceeded(configuredLimit);

    private static BasketItem? FindItem(Basket basket, Guid productId) =>
        basket.Items.FirstOrDefault(item => item.ProductId == productId);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static Result<BasketResponse> StorageFailure() =>
        Result.Failure<BasketResponse>(BasketErrors.StorageUnavailable);

    private enum MutationOutcome
    {
        Unchanged,
        Changed
    }
}
