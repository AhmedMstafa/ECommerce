using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.Domain.Specifications;
using ECommerce.UseCases.Common.Baskets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ECommerce.UnitTests.Baskets;

public class BasketServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddItemAsync_ShouldAddNewProduct_AndSnapshotPrice()
    {
        var store = new FakeBasketStore();
        var products = Substitute.For<IReadRepository<Product>>();
        var product = CreateProduct(stockQuantity: 10, price: 100m);
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([product]);

        var service = CreateService(store, products);

        var result = await service.AddItemAsync(BasketKey.ForGuest("hash"), product.Id, 2);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Items.Should().ContainSingle().Subject;
        item.ProductId.Should().Be(product.Id);
        item.Quantity.Should().Be(2);
        item.PriceAtAdd.Should().Be(100m);
        item.CurrentUnitPrice.Should().Be(100m);
        item.HasPriceChanged.Should().BeFalse();
        item.Status.Should().Be("Available");
        item.LineTotal.Should().Be(200m);
        result.Value.Subtotal.Should().Be(200m);
    }

    [Fact]
    public async Task AddItemAsync_ShouldIncreaseQuantity_WhenProductAlreadyExists()
    {
        var store = new FakeBasketStore();
        var products = Substitute.For<IReadRepository<Product>>();
        var product = CreateProduct(stockQuantity: 10, price: 100m);
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([product]);
        var service = CreateService(store, products);
        var key = BasketKey.ForGuest("hash");

        await service.AddItemAsync(key, product.Id, 2);
        var result = await service.AddItemAsync(key, product.Id, 3);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle().Which.Quantity.Should().Be(5);
    }

    [Fact]
    public async Task AddItemAsync_ShouldReject_WhenQuantityExceedsStockOrLimit()
    {
        var store = new FakeBasketStore();
        var products = Substitute.For<IReadRepository<Product>>();
        var product = CreateProduct(stockQuantity: 2, price: 100m);
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        var service = CreateService(store, products, maximumQuantityPerProduct: 5);

        var result = await service.AddItemAsync(BasketKey.ForGuest("hash"), product.Id, 3);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Basket.InsufficientStock");
    }

    [Fact]
    public async Task AddItemAsync_ShouldReject_WhenConfiguredLimitExceeded()
    {
        var store = new FakeBasketStore();
        var products = Substitute.For<IReadRepository<Product>>();
        var product = CreateProduct(stockQuantity: 100, price: 100m);
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        var service = CreateService(store, products, maximumQuantityPerProduct: 5);

        var result = await service.AddItemAsync(BasketKey.ForGuest("hash"), product.Id, 6);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Basket.MaximumQuantityExceeded");
    }

    [Fact]
    public async Task UpdateQuantityAsync_ShouldReject_WhenProductNotInBasket()
    {
        var store = new FakeBasketStore();
        var products = Substitute.For<IReadRepository<Product>>();
        var service = CreateService(store, products);

        var result = await service.UpdateQuantityAsync(BasketKey.ForGuest("hash"), Guid.NewGuid(), 2);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Basket.ItemNotFound");
    }

    [Fact]
    public async Task GetAsync_ShouldReturnEmptyBasket_WhenNoneExists()
    {
        var service = CreateService(new FakeBasketStore(), Substitute.For<IReadRepository<Product>>());

        var result = await service.GetAsync(BasketKey.ForUser(Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalItems.Should().Be(0);
        result.Value.Subtotal.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_ShouldShowPriceChange_AndRecalculateSubtotalUsingCurrentPrice()
    {
        var key = BasketKey.ForGuest("hash");
        var product = CreateProduct(stockQuantity: 10, price: 1200m);
        var basket = Basket.CreateGuest("hash", Now.UtcDateTime);
        basket.AddOrIncrease(product.Id, 2, 1000m, Now.UtcDateTime);
        var store = new FakeBasketStore(basket);
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([product]);

        var service = CreateService(store, products);

        var result = await service.GetAsync(key);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Items.Single();
        item.PriceAtAdd.Should().Be(1000m);
        item.CurrentUnitPrice.Should().Be(1200m);
        item.HasPriceChanged.Should().BeTrue();
        item.PriceDifference.Should().Be(200m);
        item.LineTotal.Should().Be(2400m);
        result.Value.Subtotal.Should().Be(2400m);
        result.Value.HasPriceChanges.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_ShouldMarkUnavailableAndOutOfStockItems_AndExcludeFromSubtotal()
    {
        var key = BasketKey.ForGuest("hash");
        var inStock = CreateProduct(stockQuantity: 5, price: 50m);
        var outOfStock = CreateProduct(stockQuantity: 0, price: 30m);
        var reduced = CreateProduct(stockQuantity: 1, price: 20m);
        var missingId = Guid.NewGuid();

        var basket = Basket.CreateGuest("hash", Now.UtcDateTime);
        basket.AddOrIncrease(inStock.Id, 2, 50m, Now.UtcDateTime);
        basket.AddOrIncrease(outOfStock.Id, 1, 30m, Now.UtcDateTime);
        basket.AddOrIncrease(reduced.Id, 3, 20m, Now.UtcDateTime);
        basket.AddOrIncrease(missingId, 1, 99m, Now.UtcDateTime);
        var store = new FakeBasketStore(basket);
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([inStock, outOfStock, reduced]);

        var service = CreateService(store, products);

        var result = await service.GetAsync(key);

        result.IsSuccess.Should().BeTrue();
        result.Value.HasUnavailableItems.Should().BeTrue();
        result.Value.Subtotal.Should().Be(120m); // 2*50 + 1*20 (reduced to available)

        result.Value.Items.Single(i => i.ProductId == inStock.Id).Status.Should().Be("Available");
        result.Value.Items.Single(i => i.ProductId == outOfStock.Id).Status.Should().Be("OutOfStock");
        result.Value.Items.Single(i => i.ProductId == reduced.Id).Status.Should().Be("QuantityReduced");
        result.Value.Items.Single(i => i.ProductId == reduced.Id).LineTotal.Should().Be(20m);
        result.Value.Items.Single(i => i.ProductId == missingId).Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task MergeAsync_ShouldCombineQuantities_ForMatchingProducts_AndPreserveUserBasket()
    {
        var userId = Guid.NewGuid();
        var userKey = BasketKey.ForUser(userId);
        var guestToken = GuestToken.Generate();
        var guestHash = GuestToken.Hash(guestToken);
        var guestKey = BasketKey.ForGuest(guestHash);

        var productA = CreateProduct(stockQuantity: 10, price: 10m);
        var productB = CreateProduct(stockQuantity: 10, price: 20m);
        var productC = CreateProduct(stockQuantity: 10, price: 30m);

        var userBasket = Basket.CreateUser(userId, Now.UtcDateTime);
        userBasket.AddOrIncrease(productA.Id, 1, 10m, Now.UtcDateTime);
        userBasket.AddOrIncrease(productC.Id, 3, 30m, Now.UtcDateTime);

        var guestBasket = Basket.CreateGuest(guestHash, Now.UtcDateTime);
        guestBasket.AddOrIncrease(productA.Id, 2, 10m, Now.UtcDateTime);
        guestBasket.AddOrIncrease(productB.Id, 1, 20m, Now.UtcDateTime);

        var store = new FakeBasketStore(userBasket, guestBasket);
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([productA, productB, productC]);

        var service = CreateService(store, products);

        var result = await service.MergeAsync(userKey, guestToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Single(i => i.ProductId == productA.Id).Quantity.Should().Be(3);
        result.Value.Items.Single(i => i.ProductId == productB.Id).Quantity.Should().Be(1);
        result.Value.Items.Single(i => i.ProductId == productC.Id).Quantity.Should().Be(3);
        (await store.GetAsync(guestKey)).Should().BeNull();
    }

    [Fact]
    public async Task MergeAsync_ShouldBeIdempotent_OnRetryAfterSuccess()
    {
        var userId = Guid.NewGuid();
        var userKey = BasketKey.ForUser(userId);
        var guestToken = GuestToken.Generate();
        var guestHash = GuestToken.Hash(guestToken);
        var product = CreateProduct(stockQuantity: 10, price: 10m);

        var guestBasket = Basket.CreateGuest(guestHash, Now.UtcDateTime);
        guestBasket.AddOrIncrease(product.Id, 2, 10m, Now.UtcDateTime);
        var store = new FakeBasketStore(guestBasket);
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([product]);
        var service = CreateService(store, products);

        await service.MergeAsync(userKey, guestToken);
        // Simulate retry where guest basket is already gone but marker exists.
        var result = await service.MergeAsync(userKey, guestToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Single().Quantity.Should().Be(2);
    }

    [Fact]
    public async Task MergeAsync_ShouldSkipUnavailableAndRespectStockLimits()
    {
        var userId = Guid.NewGuid();
        var userKey = BasketKey.ForUser(userId);
        var guestToken = GuestToken.Generate();
        var guestHash = GuestToken.Hash(guestToken);

        var limited = CreateProduct(stockQuantity: 3, price: 10m);
        var unavailable = CreateProduct(stockQuantity: 5, price: 10m);

        var userBasket = Basket.CreateUser(userId, Now.UtcDateTime);
        userBasket.AddOrIncrease(limited.Id, 2, 10m, Now.UtcDateTime);
        var guestBasket = Basket.CreateGuest(guestHash, Now.UtcDateTime);
        guestBasket.AddOrIncrease(limited.Id, 5, 10m, Now.UtcDateTime); // room = 1
        guestBasket.AddOrIncrease(unavailable.Id, 1, 10m, Now.UtcDateTime);
        var store = new FakeBasketStore(userBasket, guestBasket);

        var products = Substitute.For<IReadRepository<Product>>();
        // unavailable not returned -> treated as deleted/unavailable
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([limited]);

        var service = CreateService(store, products);
        var result = await service.MergeAsync(userKey, guestToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items.Single().Quantity.Should().Be(3);
    }

    [Fact]
    public async Task MergeAsync_ShouldPreserveAuthenticatedBasket_WhenGuestIsEmpty()
    {
        var userId = Guid.NewGuid();
        var userKey = BasketKey.ForUser(userId);
        var product = CreateProduct(stockQuantity: 10, price: 10m);
        var userBasket = Basket.CreateUser(userId, Now.UtcDateTime);
        userBasket.AddOrIncrease(product.Id, 1, 10m, Now.UtcDateTime);
        var store = new FakeBasketStore(userBasket);
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([product]);

        var service = CreateService(store, products);
        var result = await service.MergeAsync(userKey, GuestToken.Generate());

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Single().Quantity.Should().Be(1);
    }

    [Fact]
    public async Task Mutations_ShouldReturnStorageFailure_WhenStoreUnavailable()
    {
        var store = Substitute.For<IBasketStore>();
        store.GetAsync(Arg.Any<BasketKey>(), Arg.Any<CancellationToken>())
            .Returns<Task<Basket?>>(_ => throw new BasketStorageException("boom"));
        var service = CreateService(store, Substitute.For<IReadRepository<Product>>());

        var result = await service.GetAsync(BasketKey.ForGuest("hash"));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Basket.StorageUnavailable");
    }

    [Fact]
    public async Task AddItemAsync_ShouldRetryOnConflict_AndSucceed()
    {
        var product = CreateProduct(stockQuantity: 10, price: 10m);
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([product]);
        var store = new ConflictingOnceBasketStore();
        var service = CreateService(store, products);

        var result = await service.AddItemAsync(BasketKey.ForGuest("hash"), product.Id, 1);

        result.IsSuccess.Should().BeTrue();
        store.SaveAttempts.Should().BeGreaterThan(1);
    }

    private static BasketService CreateService(
        IBasketStore store,
        IReadRepository<Product> products,
        int maximumQuantityPerProduct = 20,
        int maximumItems = 50)
    {
        var settings = Options.Create(new BasketSettings
        {
            AnonymousExpirationDays = 7,
            AuthenticatedExpirationDays = 30,
            MaximumLifetimeDays = 60,
            MaximumItems = maximumItems,
            MaximumQuantityPerProduct = maximumQuantityPerProduct
        });

        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(Now);

        return new BasketService(store, products, settings, clock);
    }

    private static Product CreateProduct(int stockQuantity, decimal price)
    {
        return Product.Create(
            $"Product-{Guid.NewGuid():N}",
            "Description",
            "https://example.com/p.png",
            price,
            Guid.NewGuid(),
            Guid.NewGuid(),
            stockQuantity: stockQuantity).Value;
    }

    private sealed class FakeBasketStore : IBasketStore
    {
        private readonly Dictionary<string, Basket> _data = new(StringComparer.Ordinal);

        public FakeBasketStore(params Basket[] baskets)
        {
            foreach (var basket in baskets)
            {
                _data[KeyOf(basket)] = Clone(basket);
            }
        }

        public Task<Basket?> GetAsync(BasketKey key, CancellationToken ct = default) =>
            Task.FromResult(_data.TryGetValue(key.Value, out var basket) ? Clone(basket) : null);

        public Task<BasketSaveResult> SaveAsync(Basket basket, TimeSpan ttl, CancellationToken ct = default)
        {
            var key = KeyOf(basket);
            var currentRevision = _data.TryGetValue(key, out var current) ? current.Revision : -1;
            if (currentRevision != basket.ExpectedRevision)
            {
                return Task.FromResult(new BasketSaveResult(BasketSaveOutcome.Conflict, current is null ? null : Clone(current)));
            }

            basket.MarkPersisted();
            _data[key] = Clone(basket);
            return Task.FromResult(new BasketSaveResult(BasketSaveOutcome.Saved, Clone(basket)));
        }

        public Task<bool> DeleteAsync(BasketKey key, long expectedRevision, CancellationToken ct = default)
        {
            if (!_data.TryGetValue(key.Value, out var current))
            {
                return Task.FromResult(true);
            }

            if (current.Revision != expectedRevision)
            {
                return Task.FromResult(false);
            }

            _data.Remove(key.Value);
            return Task.FromResult(true);
        }

        private static string KeyOf(Basket basket) =>
            (basket.UserId is { } userId ? BasketKey.ForUser(userId) : BasketKey.ForGuest(basket.GuestTokenHash!)).Value;

        private static Basket Clone(Basket basket) => Basket.Rehydrate(
            basket.Id,
            basket.UserId,
            basket.GuestTokenHash,
            basket.CreatedAt,
            basket.UpdatedAt,
            basket.Revision,
            basket.Items.Select(i => BasketItem.Rehydrate(i.ProductId, i.Quantity, i.PriceAtAdd, i.AddedAt)),
            basket.MergedGuestTokenHashes);
    }

    private sealed class ConflictingOnceBasketStore : IBasketStore
    {
        private readonly FakeBasketStore _inner = new();
        private bool _conflicted;

        public int SaveAttempts { get; private set; }

        public Task<Basket?> GetAsync(BasketKey key, CancellationToken ct = default) => _inner.GetAsync(key, ct);

        public async Task<BasketSaveResult> SaveAsync(Basket basket, TimeSpan ttl, CancellationToken ct = default)
        {
            SaveAttempts++;
            if (!_conflicted)
            {
                _conflicted = true;
                return new BasketSaveResult(BasketSaveOutcome.Conflict, null);
            }

            return await _inner.SaveAsync(basket, ttl, ct);
        }

        public Task<bool> DeleteAsync(BasketKey key, long expectedRevision, CancellationToken ct = default) =>
            _inner.DeleteAsync(key, expectedRevision, ct);
    }
}
