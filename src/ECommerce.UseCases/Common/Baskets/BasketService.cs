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
    // Generous enough to absorb short bursts of concurrent optimistic writes
    // (e.g. parallel add-item requests) while keeping mutation retries bounded.
    private const int MaximumSaveAttempts = 10;
    private const int MaximumExpiryRecoveryAttempts = 3;

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
                (basket, now, _) =>
                {
                    var removeResult = basket.Remove(productId, now);
                    if (removeResult.IsFailure)
                    {
                        return Task.FromResult<Result<MutationOutcome>>(removeResult.Error!);
                    }

                    return Task.FromResult<Result<MutationOutcome>>(MutationOutcome.Changed);
                },
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

                    var setResult = basket.SetQuantity(productId, quantity, product.Price, now);
                    return setResult.IsFailure ? setResult.Error! : MutationOutcome.Changed;
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
            var guestKey = BasketKey.ForGuest(guestTokenHash);
            Basket? lastMergedSnapshot = null;

            for (var attempt = 0; attempt < MaximumSaveAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var userBasket = await LoadAsync(userKey, ct) ?? CreateBasket(userKey, Now);
                var guestBasket = await LoadAsync(guestKey, ct);

                if (guestBasket is null || guestBasket.Items.Count == 0)
                {
                    // Nothing left to merge; preserve authenticated basket.
                    return Result.Success(await BuildResponseAsync(userBasket, ct));
                }

                // This exact guest revision was already applied; complete cleanup.
                if (userBasket.HasMergedGuestRevision(guestTokenHash, guestBasket.Revision))
                {
                    try
                    {
                        await store.DeleteAsync(guestKey, guestBasket.Revision, ct);
                    }
                    catch (BasketStorageException)
                    {
                        // Best-effort completion after a previously persisted merge.
                    }

                    return Result.Success(await BuildResponseAsync(userBasket, ct));
                }

                // Legacy marker (hash-only, from older persisted state): treat as
                // already merged to avoid duplicate quantities. Best-effort cleanup.
                if (userBasket.HasMergedGuest(guestTokenHash) &&
                    !userBasket.HasMergedGuestRevision(guestTokenHash, guestBasket.Revision))
                {
                    try
                    {
                        await store.DeleteAsync(guestKey, guestBasket.Revision, ct);
                    }
                    catch (BasketStorageException)
                    {
                    }

                    return Result.Success(await BuildResponseAsync(userBasket, ct));
                }

                var mergeSource = guestBasket;
                if (lastMergedSnapshot is not null && guestBasket.Revision != lastMergedSnapshot.Revision)
                {
                    // Guest changed after a revision we already applied to the user
                    // basket but failed to consume/delete. Merge only additive deltas
                    // relative to that applied snapshot to avoid losing newer updates
                    // without duplicating previously merged quantities.
                    mergeSource = ComputeAdditiveDelta(lastMergedSnapshot, guestBasket);
                    if (mergeSource.Items.Count == 0)
                    {
                        // No additive updates to apply; try to consume current revision.
                        try
                        {
                            var deleted = await store.DeleteAsync(guestKey, guestBasket.Revision, ct);
                            if (deleted)
                            {
                                userBasket.MarkGuestMergedRevision(guestTokenHash, guestBasket.Revision, Now);
                                var save = await store.SaveAsync(userBasket, _settings.SlidingExpiration(BasketKind.User), ct);
                                if (save.Outcome == BasketSaveOutcome.Conflict)
                                {
                                    continue;
                                }

                                if (save.Outcome == BasketSaveOutcome.Failed)
                                {
                                    return StorageFailure();
                                }

                                return Result.Success(await BuildResponseAsync(userBasket, ct));
                            }
                        }
                        catch (BasketStorageException)
                        {
                        }

                        lastMergedSnapshot = guestBasket;
                        continue;
                    }
                }

                var mergeResult = await MergeGuestIntoUserAsync(userBasket, mergeSource, guestTokenHash, guestBasket.Revision, ct);
                if (mergeResult.IsFailure)
                {
                    return Result.Failure<BasketResponse>(mergeResult.Error!);
                }

                var persisted = mergeResult.Value;
                if (persisted is null)
                {
                    // No effective changes from this merge source; nothing to persist.
                    return Result.Success(await BuildResponseAsync(userBasket, ct));
                }

                // Attempt to consume exactly the guest revision we observed/merged.
                bool guestDeleted;
                try
                {
                    guestDeleted = await store.DeleteAsync(guestKey, guestBasket.Revision, ct);
                }
                catch (BasketStorageException)
                {
                    // User basket merge (including revision marker) is already
                    // persisted. Cleanup is best-effort; retries can complete it.
                    return Result.Success(await BuildResponseAsync(persisted, ct));
                }

                if (guestDeleted)
                {
                    return Result.Success(await BuildResponseAsync(persisted, ct));
                }

                // CAS conflict: guest changed since we read it. The persisted merge
                // covered only guestBasket.Revision (or its delta basis), so loop to
                // incorporate any newer additive updates without deleting unmerged state.
                lastMergedSnapshot = guestBasket;
            }

            return Result.Failure<BasketResponse>(BasketErrors.ConcurrentUpdate);
        }
        catch (BasketStorageException)
        {
            return StorageFailure();
        }
    }

    private async Task<Result<Basket?>> MergeGuestIntoUserAsync(
        Basket userBasket,
        Basket mergeSource,
        string guestTokenHash,
        long consumedGuestRevision,
        CancellationToken ct)
    {
        var now = Now;
        var productIds = mergeSource.Items.Select(item => item.ProductId).Distinct().ToArray();
        if (productIds.Length == 0)
        {
            return Result.Success<Basket?>(null);
        }

        var products = await productRepository.GetAllWithSpecAsync(
            new ProductsByIdsSpecification(productIds),
            ct);
        var productById = products.ToDictionary(product => product.Id);

        var changed = false;
        foreach (var item in mergeSource.Items)
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
            var addResult = userBasket.AddOrIncrease(item.ProductId, quantity, product.Price, now);
            if (addResult.IsFailure)
            {
                return Result.Failure<Basket?>(addResult.Error!);
            }

            changed = true;
        }

        if (!changed)
        {
            return Result.Success<Basket?>(null);
        }

        userBasket.MarkGuestMergedRevision(guestTokenHash, consumedGuestRevision, now);
        var save = await store.SaveAsync(
            userBasket,
            _settings.SlidingExpiration(BasketKind.User),
            ct);

        if (save.Outcome == BasketSaveOutcome.Failed)
        {
            return Result.Failure<Basket?>(BasketErrors.StorageUnavailable);
        }

        if (save.Outcome == BasketSaveOutcome.Conflict)
        {
            return Result.Failure<Basket?>(BasketErrors.ConcurrentUpdate);
        }

        return Result.Success<Basket?>(userBasket);
    }

    private static Basket ComputeAdditiveDelta(Basket appliedSnapshot, Basket currentGuest)
    {
        var delta = Basket.CreateGuest(appliedSnapshot.GuestTokenHash ?? string.Empty, currentGuest.UpdatedAt);
        foreach (var item in currentGuest.Items)
        {
            var appliedQuantity = appliedSnapshot.Items.FirstOrDefault(i => i.ProductId == item.ProductId)?.Quantity ?? 0;
            var additional = item.Quantity - appliedQuantity;
            if (additional > 0)
            {
                delta.AddOrIncrease(item.ProductId, additional, item.PriceAtAdd, currentGuest.UpdatedAt);
            }
            else if (appliedQuantity == 0)
            {
                // New product not in applied snapshot.
                delta.AddOrIncrease(item.ProductId, item.Quantity, item.PriceAtAdd, currentGuest.UpdatedAt);
            }
        }

        // Rehydrate revision to 0/empty state is fine; delta is never persisted.
        return delta;
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
        var current = await store.GetAsync(key, ct);
        if (current is null)
        {
            return null;
        }

        if (!IsExpired(current))
        {
            return current;
        }

        for (var attempt = 0; attempt < MaximumExpiryRecoveryAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var expired = current;

            var deleted = await store.DeleteAsync(key, expired.Revision, ct);

            current = await store.GetAsync(key, ct);
            if (current is null)
            {
                return null;
            }

            if (!IsExpired(current))
            {
                return current;
            }

            if (current.Revision != expired.Revision)
            {
                // A concurrent writer produced a newer revision that is still
                // expired under the hard absolute-lifetime rule. Retry recovery
                // against this exact observed revision.
                continue;
            }

            // The same expired revision is still present after a revision-aware
            // delete attempt. This is contradictory/unprogressable: CAS conflict
            // with an unchanged revision, or a reported delete without authoritative
            // removal. Do not return expired contents or create an ExpectedRevision=-1
            // insert while the key still exists.
            _ = deleted;
            throw new BasketStorageException(
                "Failed to recover an expired basket because its stored revision could not be removed safely.");
        }

        throw new BasketStorageException(
            "Failed to recover an expired basket within the bounded recovery attempts.");
    }

    private bool IsExpired(Basket basket) =>
        Now - basket.CreatedAt >= _settings.MaximumLifetime;

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
