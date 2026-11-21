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
    public async Task Mutations_ShouldReturnStorageFailure_WhenDeleteDuringExpiryRecoveryFailsToRead()
    {
        var key = BasketKey.ForGuest("hash");
        var expired = Basket.Rehydrate(
            Guid.NewGuid(),
            null,
            "hash",
            Now.UtcDateTime.AddDays(-61),
            Now.UtcDateTime.AddDays(-30),
            1,
            [],
            []);
        var store = Substitute.For<IBasketStore>();
        store.GetAsync(key, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(expired), Task.FromException<Basket?>(new BasketStorageException("boom")));
        store.DeleteAsync(key, expired.Revision, Arg.Any<CancellationToken>()).Returns(true);

        var service = CreateService(store, Substitute.For<IReadRepository<Product>>());
        var result = await service.AddItemAsync(key, Guid.NewGuid(), 1);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Basket.StorageUnavailable");
    }

    [Fact]
    public async Task AddItemAsync_ShouldRecoverExpiredBasket_WhenKeyStillExists()
    {
        var key = BasketKey.ForGuest("hash");
        var product = CreateProduct(stockQuantity: 10, price: 25m);
        var expired = Basket.Rehydrate(
            Guid.NewGuid(),
            null,
            "hash",
            Now.UtcDateTime.AddDays(-61),
            Now.UtcDateTime.AddDays(-30),
            3,
            [BasketItem.Rehydrate(product.Id, 1, 25m, Now.UtcDateTime.AddDays(-30))],
            []);
        var store = new FakeBasketStore(expired);
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([product]);
        var service = CreateService(store, products);

        var result = await service.AddItemAsync(key, product.Id, 2);

        // Hard expiry: expired contents must never be returned or used as a base.
        // Recovery removes the expired key and the mutation creates a fresh basket.
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
        var persisted = await store.GetAsync(key);
        persisted.Should().NotBeNull();
        persisted!.CreatedAt.Should().Be(Now.UtcDateTime);
        persisted.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
    }

    [Fact]
    public async Task RepeatedMutations_ShouldSucceed_AfterExpiredBasketRecovery()
    {
        var key = BasketKey.ForGuest("hash");
        var product = CreateProduct(stockQuantity: 10, price: 25m);
        var expired = Basket.Rehydrate(
            Guid.NewGuid(),
            null,
            "hash",
            Now.UtcDateTime.AddDays(-61),
            Now.UtcDateTime.AddDays(-30),
            4,
            [],
            []);
        var store = new FakeBasketStore(expired);
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([product]);
        var service = CreateService(store, products);

        var first = await service.AddItemAsync(key, product.Id, 1);
        var second = await service.AddItemAsync(key, product.Id, 2);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.Items.Should().ContainSingle().Which.Quantity.Should().Be(3);
    }

    [Fact]
    public async Task AddItemAsync_ShouldRecover_WhenConcurrentWriterCreatesNewerButStillExpiredRevision_T5()
    {
        var key = BasketKey.ForGuest("hash");
        var product = CreateProduct(stockQuantity: 10, price: 25m);
        var expired = Basket.Rehydrate(
            Guid.NewGuid(),
            null,
            "hash",
            Now.UtcDateTime.AddDays(-61),
            Now.UtcDateTime.AddDays(-30),
            5,
            [BasketItem.Rehydrate(product.Id, 1, 25m, Now.UtcDateTime.AddDays(-30))],
            []);

        // Concurrent mutation after read/delete attempt: revision changes but basket remains age-expired.
        var store = new ScriptedBasketStore(
            initial: expired,
            onDelete: (inner, k, expectedRevision, ct) =>
            {
                var current = inner.GetCurrent(k);
                if (current is not null && current.Revision == expectedRevision && expectedRevision == expired.Revision)
                {
                    current.AddOrIncrease(Guid.NewGuid(), 1, 30m, Now.UtcDateTime.AddDays(-1));
                    inner.SaveInternally(current);
                }

                // CAS delete for the observed (possibly now stale) revision.
                return Task.FromResult(inner.TryDelete(k, expectedRevision));
            });

        var products = Substitute.For<IReadRepository<Product>>();
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([product]);
        var service = CreateService(store, products);

        var result = await service.AddItemAsync(key, product.Id, 2);

        result.IsSuccess.Should().BeTrue();
        // Expired contents (original and concurrent-still-expired) must not be exposed; recovery yields a fresh basket.
        result.Value.Items.Should().ContainSingle();
        result.Value.Items.Single().ProductId.Should().Be(product.Id);
        result.Value.Items.Single().Quantity.Should().Be(2);
        (await store.GetAsync(key))!.CreatedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task AddItemAsync_ShouldReturnStorageFailure_WhenCasDeleteReportsConflictButSameExpiredRevisionRemains_T2()
    {
        var key = BasketKey.ForGuest("hash");
        var product = CreateProduct(stockQuantity: 10, price: 25m);
        var expired = Basket.Rehydrate(
            Guid.NewGuid(),
            null,
            "hash",
            Now.UtcDateTime.AddDays(-61),
            Now.UtcDateTime.AddDays(-30),
            6,
            [BasketItem.Rehydrate(product.Id, 1, 25m, Now.UtcDateTime.AddDays(-30))],
            []);

        var store = new ScriptedBasketStore(
            initial: expired,
            onDelete: (_, _, _, _) => Task.FromResult(false)); // CAS conflict, state unchanged

        var products = Substitute.For<IReadRepository<Product>>();
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        var service = CreateService(store, products);

        var result = await service.AddItemAsync(key, product.Id, 1);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Basket.StorageUnavailable");
        // No expired contents exposed and no ExpectedRevision=-1 insert created over existing key.
        var persisted = store.GetCurrent(key);
        persisted.Should().NotBeNull();
        persisted!.Revision.Should().Be(expired.Revision);
        persisted.CreatedAt.Should().Be(expired.CreatedAt);
        persisted.Items.Should().ContainSingle().Which.Quantity.Should().Be(1);
    }

    [Fact]
    public async Task AddItemAsync_ShouldRecoverExpiredAuthenticatedBasket_WhenKeyStillExists()
    {
        var userId = Guid.NewGuid();
        var key = BasketKey.ForUser(userId);
        var product = CreateProduct(stockQuantity: 10, price: 25m);
        var expired = Basket.Rehydrate(
            Guid.NewGuid(),
            userId,
            null,
            Now.UtcDateTime.AddDays(-61),
            Now.UtcDateTime.AddDays(-30),
            2,
            [BasketItem.Rehydrate(product.Id, 1, 25m, Now.UtcDateTime.AddDays(-30))],
            []);
        var store = new FakeBasketStore(expired);
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([product]);
        var service = CreateService(store, products);

        var result = await service.AddItemAsync(key, product.Id, 2);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(userId);
        result.Value.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
        var persisted = await store.GetAsync(key);
        persisted.Should().NotBeNull();
        persisted!.UserId.Should().Be(userId);
        persisted.CreatedAt.Should().Be(Now.UtcDateTime);
        persisted.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
    }

    [Fact]
    public async Task MergeAsync_ShouldRecoverExpiredAuthenticatedBasket_WhenKeyStillExists()
    {
        var userId = Guid.NewGuid();
        var userKey = BasketKey.ForUser(userId);
        var guestToken = GuestToken.Generate();
        var guestHash = GuestToken.Hash(guestToken);
        var product = CreateProduct(stockQuantity: 10, price: 10m);

        var expiredUser = Basket.Rehydrate(
            Guid.NewGuid(),
            userId,
            null,
            Now.UtcDateTime.AddDays(-61),
            Now.UtcDateTime.AddDays(-30),
            2,
            [BasketItem.Rehydrate(Guid.NewGuid(), 1, 5m, Now.UtcDateTime.AddDays(-30))],
            []);
        var guestBasket = Basket.CreateGuest(guestHash, Now.UtcDateTime);
        guestBasket.AddOrIncrease(product.Id, 2, 10m, Now.UtcDateTime);

        var store = new FakeBasketStore(expiredUser, guestBasket);
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns([product]);
        var service = CreateService(store, products);

        var result = await service.MergeAsync(userKey, guestToken);

        // Expired authenticated contents must not be merged/returned; recovery yields a fresh user base.
        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(userId);
        result.Value.Items.Should().ContainSingle();
        result.Value.Items.Single().ProductId.Should().Be(product.Id);
        result.Value.Items.Single().Quantity.Should().Be(2);
        (await store.GetAsync(BasketKey.ForGuest(guestHash))).Should().BeNull();
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

    // -----------------------
    // T1â€“T14 expiry recovery
    // -----------------------

    [Fact]
    public async Task T1_GetAsync_ShouldReturnStorageFailure_WhenDeleteThrowsInfraForSameExpiredRevision()
    {
        var key = BasketKey.ForGuest("hash");
        var expired = CreateExpiredGuestBasket("hash", revision: 7, itemProductId: Guid.NewGuid());
        var store = new ScriptedBasketStore(expired, onDelete: (_, _, _, _) => throw new BasketStorageException("redis down"));
        var service = CreateService(store, Substitute.For<IReadRepository<Product>>());

        var result = await service.GetAsync(key);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Basket.StorageUnavailable");
    }

    [Fact]
    public async Task T1_AddItemAsync_ShouldReturnStorageFailure_WhenDeleteThrowsInfraForSameExpiredRevision()
    {
        var key = BasketKey.ForGuest("hash");
        var product = CreateProduct(10, 10m);
        var expired = CreateExpiredGuestBasket("hash", revision: 7, itemProductId: product.Id);
        var store = new ScriptedBasketStore(expired, onDelete: (_, _, _, _) => throw new BasketStorageException("redis down"));
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        var service = CreateService(store, products);

        var result = await service.AddItemAsync(key, product.Id, 1);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Basket.StorageUnavailable");
        store.GetCurrent(key)!.Revision.Should().Be(expired.Revision);
        store.SaveAttempts.Should().Be(0);
    }

    [Fact]
    public async Task T1_MergeAsync_ShouldReturnStorageFailure_WhenExpiredGuestDeleteThrowsInfra()
    {
        var userId = Guid.NewGuid();
        var userKey = BasketKey.ForUser(userId);
        var guestToken = GuestToken.Generate();
        var guestHash = GuestToken.Hash(guestToken);
        var guestProduct = CreateProduct(10, 10m);

        var userBasket = Basket.CreateUser(userId, Now.UtcDateTime);
        var expiredGuest = CreateExpiredGuestBasket(guestHash, revision: 4, itemProductId: guestProduct.Id);
        var inner = new InMemoryDictionaryBasketStore(userBasket, expiredGuest);
        var store = new ScriptedBasketStore(inner, onDelete: (s, k, r, _) =>
            k.Kind == BasketKind.Guest ? throw new BasketStorageException("redis down") : Task.FromResult(s.TryDelete(k, r)));

        var products = Substitute.For<IReadRepository<Product>>();
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>()).Returns([guestProduct]);
        var service = CreateService(store, products);

        var result = await service.MergeAsync(userKey, guestToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Basket.StorageUnavailable");
        // Expired guest contents must not be merged; user basket remains unchanged.
        inner.GetCurrent(userKey)!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task T3_AddItemAsync_ShouldFailCurrentAttemptOnTransientDeleteInfraFailure_AndSucceedOnRetryWhenRecoverable()
    {
        var key = BasketKey.ForGuest("hash");
        var product = CreateProduct(10, 10m);
        var expired = CreateExpiredGuestBasket("hash", revision: 3, itemProductId: product.Id);
        var store = new TransientThenRecoverableDeleteStore(expired);
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>()).Returns([product]);
        var service = CreateService(store, products);

        var first = await service.AddItemAsync(key, product.Id, 1);
        first.IsFailure.Should().BeTrue();
        first.Error!.Code.Should().Be("Basket.StorageUnavailable");

        var second = await service.AddItemAsync(key, product.Id, 1);
        second.IsSuccess.Should().BeTrue();
        second.Value.Items.Should().ContainSingle().Which.Quantity.Should().Be(1);
        store.DeleteAttempts.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task T4_GetAsync_ShouldPreserveValidNonExpiredReplacement_WhenDeleteCasConflicts()
    {
        var key = BasketKey.ForGuest("hash");
        var product = CreateProduct(10, 5m);
        var expired = CreateExpiredGuestBasket("hash", revision: 5, itemProductId: Guid.NewGuid());
        var replacement = Basket.Rehydrate(
            Guid.NewGuid(), null, "hash",
            Now.UtcDateTime.AddMinutes(-5), Now.UtcDateTime,
            6, [BasketItem.Rehydrate(product.Id, 1, 5m, Now.UtcDateTime)], []);

        var store = new ScriptedBasketStore(
            initial: expired,
            onDelete: (inner, k, _, _) =>
            {
                // Concurrent writer replaced with a valid non-expired basket.
                inner.Replace(k, replacement);
                return Task.FromResult(false); // CAS mismatch for observed expired revision
            });

        var products = Substitute.For<IReadRepository<Product>>();
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>()).Returns([product]);
        var service = CreateService(store, products);
        var result = await service.GetAsync(key);

        result.IsSuccess.Should().BeTrue();
        result.Value.CreatedAt.Should().Be(replacement.CreatedAt);
        result.Value.Items.Should().ContainSingle();
        result.Value.Items.Single().ProductId.Should().Be(product.Id);
    }

    [Fact]
    public async Task T6_AddItemAsync_ShouldReturnStorageFailure_WhenNewerStillExpiredRevisionsExceedBound()
    {
        var key = BasketKey.ForGuest("hash");
        var product = CreateProduct(10, 10m);
        var expired = CreateExpiredGuestBasket("hash", revision: 1, itemProductId: Guid.NewGuid());
        var store = new AlwaysStillExpiredConcurrentWriterStore(expired, Now.UtcDateTime.AddDays(-61));
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        var service = CreateService(store, products);

        var result = await service.AddItemAsync(key, product.Id, 1);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Basket.StorageUnavailable");
        store.DeleteAttempts.Should().Be(3);
        store.GetCurrent(key)!.CreatedAt.Should().Be(Now.UtcDateTime.AddDays(-61)); // still expired, not extended
    }

    [Fact]
    public async Task T7_GetAsync_ShouldReturnValidReplacement_WhenReReadAfterDeleteIsNonExpired()
    {
        var key = BasketKey.ForGuest("hash");
        var expired = CreateExpiredGuestBasket("hash", revision: 2, itemProductId: Guid.NewGuid());
        var replacement = Basket.Rehydrate(
            Guid.NewGuid(), null, "hash",
            Now.UtcDateTime, Now.UtcDateTime,
            3, [], []);

        var store = new ScriptedBasketStore(
            initial: expired,
            onDelete: (inner, k, expectedRevision, _) =>
            {
                var deleted = inner.TryDelete(k, expectedRevision);
                inner.Replace(k, replacement);
                return Task.FromResult(deleted);
            });

        var service = CreateService(store, Substitute.For<IReadRepository<Product>>());
        var result = await service.GetAsync(key);

        result.IsSuccess.Should().BeTrue();
        result.Value.CreatedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task T8_GetAsync_ShouldNotReturnExpiredGuestContents_WhenRecoverable()
    {
        var key = BasketKey.ForGuest("hash");
        var product = CreateProduct(10, 10m);
        var expired = CreateExpiredGuestBasket("hash", revision: 1, itemProductId: product.Id, quantity: 3);
        var store = new FakeBasketStore(expired);
        var service = CreateService(store, Substitute.For<IReadRepository<Product>>());

        var result = await service.GetAsync(key);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task T9_GetAsync_ShouldNotReturnExpiredAuthenticatedContents_WhenRecoverable()
    {
        var userId = Guid.NewGuid();
        var key = BasketKey.ForUser(userId);
        var expired = Basket.Rehydrate(
            Guid.NewGuid(), userId, null,
            Now.UtcDateTime.AddDays(-61), Now.UtcDateTime.AddDays(-30), 2,
            [BasketItem.Rehydrate(Guid.NewGuid(), 2, 5m, Now.UtcDateTime.AddDays(-30))], []);
        var store = new FakeBasketStore(expired);
        var service = CreateService(store, Substitute.For<IReadRepository<Product>>());

        var result = await service.GetAsync(key);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(userId);
        result.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task T10_GetAsync_ShouldNotExposeExpiredContents_WhenUnrecoverable()
    {
        var key = BasketKey.ForGuest("hash");
        var expired = CreateExpiredGuestBasket("hash", revision: 1, itemProductId: Guid.NewGuid(), quantity: 2);
        var store = new ScriptedBasketStore(expired, onDelete: (_, _, _, _) => Task.FromResult(false));
        var service = CreateService(store, Substitute.For<IReadRepository<Product>>());

        var result = await service.GetAsync(key);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Basket.StorageUnavailable");
    }

    [Fact]
    public async Task T11_MergeAsync_ShouldNotMergeExpiredGuestContents_WhenRecoverable()
    {
        var userId = Guid.NewGuid();
        var userKey = BasketKey.ForUser(userId);
        var guestToken = GuestToken.Generate();
        var guestHash = GuestToken.Hash(guestToken);
        var userProduct = CreateProduct(10, 10m);
        var guestProduct = CreateProduct(10, 20m);

        var userBasket = Basket.CreateUser(userId, Now.UtcDateTime);
        userBasket.AddOrIncrease(userProduct.Id, 1, 10m, Now.UtcDateTime);
        var expiredGuest = CreateExpiredGuestBasket(guestHash, revision: 4, itemProductId: guestProduct.Id, quantity: 2);
        var store = new FakeBasketStore(userBasket, expiredGuest);

        var products = Substitute.For<IReadRepository<Product>>();
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>()).Returns([userProduct, guestProduct]);
        var service = CreateService(store, products);

        var result = await service.MergeAsync(userKey, guestToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items.Single().ProductId.Should().Be(userProduct.Id);
        result.Value.Items.Single().Quantity.Should().Be(1);
    }

    [Fact]
    public async Task T12_MergeAsync_ShouldNotMergeExpiredUserContents_WhenRecoverable()
    {
        var userId = Guid.NewGuid();
        var userKey = BasketKey.ForUser(userId);
        var guestToken = GuestToken.Generate();
        var guestHash = GuestToken.Hash(guestToken);
        var guestProduct = CreateProduct(10, 10m);
        var staleUserProductId = Guid.NewGuid();

        var expiredUser = Basket.Rehydrate(
            Guid.NewGuid(), userId, null,
            Now.UtcDateTime.AddDays(-61), Now.UtcDateTime.AddDays(-30), 2,
            [BasketItem.Rehydrate(staleUserProductId, 5, 5m, Now.UtcDateTime.AddDays(-30))], []);
        var guestBasket = Basket.CreateGuest(guestHash, Now.UtcDateTime);
        guestBasket.AddOrIncrease(guestProduct.Id, 2, 10m, Now.UtcDateTime);
        var store = new FakeBasketStore(expiredUser, guestBasket);

        var products = Substitute.For<IReadRepository<Product>>();
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>()).Returns([guestProduct]);
        var service = CreateService(store, products);

        var result = await service.MergeAsync(userKey, guestToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items.Single().ProductId.Should().Be(guestProduct.Id);
        result.Value.Items.Single().Quantity.Should().Be(2);
        result.Value.Items.Select(i => i.ProductId).Should().NotContain(staleUserProductId);
    }

    [Fact]
    public async Task T13_MergeAsync_ShouldSucceed_WhenPostSaveGuestCleanupDeleteThrowsInfra()
    {
        var userId = Guid.NewGuid();
        var userKey = BasketKey.ForUser(userId);
        var guestToken = GuestToken.Generate();
        var guestHash = GuestToken.Hash(guestToken);
        var guestKey = BasketKey.ForGuest(guestHash);
        var product = CreateProduct(10, 10m);

        var guestBasket = Basket.CreateGuest(guestHash, Now.UtcDateTime);
        guestBasket.AddOrIncrease(product.Id, 2, 10m, Now.UtcDateTime);
        var inner = new InMemoryDictionaryBasketStore(guestBasket);
        var store = new ScriptedBasketStore(inner, onDelete: (s, k, r, _) =>
            k == guestKey ? throw new BasketStorageException("cleanup failed") : Task.FromResult(s.TryDelete(k, r)));

        var products = Substitute.For<IReadRepository<Product>>();
        products.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>()).Returns([product]);
        var service = CreateService(store, products);

        var result = await service.MergeAsync(userKey, guestToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
        // User basket save and idempotency marker remain authoritative.
        var persistedUser = inner.GetCurrent(userKey);
        persistedUser.Should().NotBeNull();
        persistedUser!.HasMergedGuest(guestHash).Should().BeTrue();
        inner.GetCurrent(guestKey).Should().NotBeNull();
    }

    [Fact]
    public async Task T14_AddItemAsync_ShouldPropagateCancellation_DuringExpiryRecoveryDelete()
    {
        var key = BasketKey.ForGuest("hash");
        var product = CreateProduct(10, 10m);
        var expired = CreateExpiredGuestBasket("hash", revision: 1, itemProductId: product.Id);
        using var cts = new CancellationTokenSource();
        var store = new ScriptedBasketStore(expired, onDelete: (_, _, _, _) => throw new OperationCanceledException(cts.Token));
        var products = Substitute.For<IReadRepository<Product>>();
        products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        var service = CreateService(store, products);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AddItemAsync(key, product.Id, 1, cts.Token));
    }

    [Fact]
    public async Task T14_GetAsync_ShouldReturnStorageFailure_WhenReadFailsWithBasketStorageException()
    {
        var key = BasketKey.ForGuest("hash");
        var store = Substitute.For<IBasketStore>();
        store.GetAsync(key, Arg.Any<CancellationToken>()).Returns(Task.FromException<Basket?>(new BasketStorageException("boom")));
        var service = CreateService(store, Substitute.For<IReadRepository<Product>>());

        var result = await service.GetAsync(key);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Basket.StorageUnavailable");
    }

    [Fact]
    public async Task T14_MergeAsync_ShouldPropagateCancellation_DuringRecoveryRead()
    {
        var userKey = BasketKey.ForUser(Guid.NewGuid());
        var guestToken = GuestToken.Generate();
        using var cts = new CancellationTokenSource();
        var store = Substitute.For<IBasketStore>();
        store.GetAsync(Arg.Any<BasketKey>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Basket?>(new OperationCanceledException(cts.Token)));
        var service = CreateService(store, Substitute.For<IReadRepository<Product>>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.MergeAsync(userKey, guestToken, cts.Token));
    }

    private static Basket CreateExpiredGuestBasket(string guestHash, long revision, Guid itemProductId, int quantity = 1) =>
        Basket.Rehydrate(
            Guid.NewGuid(),
            null,
            guestHash,
            Now.UtcDateTime.AddDays(-61),
            Now.UtcDateTime.AddDays(-30),
            revision,
            [BasketItem.Rehydrate(itemProductId, quantity, 10m, Now.UtcDateTime.AddDays(-30))],
            []);

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

    private static Basket Clone(Basket basket) => Basket.Rehydrate(
        basket.Id,
        basket.UserId,
        basket.GuestTokenHash,
        basket.CreatedAt,
        basket.UpdatedAt,
        basket.Revision,
        basket.Items.Select(i => BasketItem.Rehydrate(i.ProductId, i.Quantity, i.PriceAtAdd, i.AddedAt)),
        basket.MergedGuestTokenHashes);

    private sealed class InMemoryDictionaryBasketStore : IBasketStore
    {
        private readonly Dictionary<string, Basket> _data = new(StringComparer.Ordinal);

        public int SaveAttempts { get; private set; }

        public InMemoryDictionaryBasketStore(params Basket[] baskets)
        {
            foreach (var basket in baskets)
            {
                _data[KeyOf(basket).Value] = Clone(basket);
            }
        }

        public Task<Basket?> GetAsync(BasketKey key, CancellationToken ct = default) =>
            Task.FromResult(_data.TryGetValue(key.Value, out var basket) ? Clone(basket) : null);

        public Basket? GetCurrent(BasketKey key) =>
            _data.TryGetValue(key.Value, out var basket) ? Clone(basket) : null;

        public void Replace(BasketKey key, Basket basket) => _data[key.Value] = Clone(basket);

        public void SaveInternally(Basket basket) => SaveCore(basket);

        public Task<BasketSaveResult> SaveAsync(Basket basket, TimeSpan ttl, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            SaveAttempts++;
            return Task.FromResult(SaveCore(basket));
        }

        private BasketSaveResult SaveCore(Basket basket)
        {
            var key = KeyOf(basket);
            var currentRevision = _data.TryGetValue(key.Value, out var current) ? current.Revision : -1;
            if (currentRevision != basket.ExpectedRevision)
            {
                return new BasketSaveResult(BasketSaveOutcome.Conflict, current is null ? null : Clone(current));
            }

            basket.MarkPersisted();
            _data[key.Value] = Clone(basket);
            return new BasketSaveResult(BasketSaveOutcome.Saved, Clone(basket));
        }

        public Task<bool> DeleteAsync(BasketKey key, long expectedRevision, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(TryDelete(key, expectedRevision));
        }

        public bool TryDelete(BasketKey key, long expectedRevision)
        {
            if (!_data.TryGetValue(key.Value, out var current))
            {
                return true;
            }

            if (current.Revision != expectedRevision)
            {
                return false;
            }

            _data.Remove(key.Value);
            return true;
        }

        private static BasketKey KeyOf(Basket basket) =>
            basket.UserId is { } userId
                ? BasketKey.ForUser(userId)
                : BasketKey.ForGuest(basket.GuestTokenHash!);
    }

    private sealed class ScriptedBasketStore : IBasketStore
    {
        private readonly InMemoryDictionaryBasketStore _inner;
        private readonly Func<InMemoryDictionaryBasketStore, BasketKey, long, CancellationToken, Task<bool>> _onDelete;

        public ScriptedBasketStore(
            Basket initial,
            Func<InMemoryDictionaryBasketStore, BasketKey, long, CancellationToken, Task<bool>> onDelete)
            : this(new InMemoryDictionaryBasketStore(initial), onDelete)
        {
        }

        public ScriptedBasketStore(
            InMemoryDictionaryBasketStore inner,
            Func<InMemoryDictionaryBasketStore, BasketKey, long, CancellationToken, Task<bool>> onDelete)
        {
            _inner = inner;
            _onDelete = onDelete;
        }

        public int SaveAttempts => _inner.SaveAttempts;

        public Task<Basket?> GetAsync(BasketKey key, CancellationToken ct = default) => _inner.GetAsync(key, ct);

        public Basket? GetCurrent(BasketKey key) => _inner.GetCurrent(key);

        public Task<BasketSaveResult> SaveAsync(Basket basket, TimeSpan ttl, CancellationToken ct = default) =>
            _inner.SaveAsync(basket, ttl, ct);

        public Task<bool> DeleteAsync(BasketKey key, long expectedRevision, CancellationToken ct = default) =>
            _onDelete(_inner, key, expectedRevision, ct);
    }

    private sealed class TransientThenRecoverableDeleteStore : IBasketStore
    {
        private readonly InMemoryDictionaryBasketStore _inner;
        private bool _thrown;

        public TransientThenRecoverableDeleteStore(Basket expired)
        {
            _inner = new InMemoryDictionaryBasketStore(expired);
        }

        public int DeleteAttempts { get; private set; }

        public Task<Basket?> GetAsync(BasketKey key, CancellationToken ct = default) => _inner.GetAsync(key, ct);

        public Task<BasketSaveResult> SaveAsync(Basket basket, TimeSpan ttl, CancellationToken ct = default) =>
            _inner.SaveAsync(basket, ttl, ct);

        public Task<bool> DeleteAsync(BasketKey key, long expectedRevision, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            DeleteAttempts++;
            if (!_thrown)
            {
                _thrown = true;
                throw new BasketStorageException("transient delete failure");
            }

            return Task.FromResult(_inner.TryDelete(key, expectedRevision));
        }
    }

    private sealed class AlwaysStillExpiredConcurrentWriterStore : IBasketStore
    {
        private readonly InMemoryDictionaryBasketStore _inner = new();
        private readonly DateTime _expiredCreatedAt;

        public AlwaysStillExpiredConcurrentWriterStore(Basket expired, DateTime expiredCreatedAt)
        {
            _expiredCreatedAt = expiredCreatedAt;
            _inner.Replace(KeyOf(expired), expired);
        }

        public int DeleteAttempts { get; private set; }

        public Task<Basket?> GetAsync(BasketKey key, CancellationToken ct = default) => _inner.GetAsync(key, ct);

        public Basket? GetCurrent(BasketKey key) => _inner.GetCurrent(key);

        public Task<BasketSaveResult> SaveAsync(Basket basket, TimeSpan ttl, CancellationToken ct = default) =>
            _inner.SaveAsync(basket, ttl, ct);

        public Task<bool> DeleteAsync(BasketKey key, long expectedRevision, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            DeleteAttempts++;
            var current = _inner.GetCurrent(key);
            if (current is not null && current.Revision == expectedRevision)
            {
                // Concurrent writer creates a newer persisted revision that is still expired.
                var next = Basket.Rehydrate(
                    current.Id,
                    current.UserId,
                    current.GuestTokenHash,
                    _expiredCreatedAt,
                    current.UpdatedAt,
                    current.Revision,
                    current.Items.Select(i => BasketItem.Rehydrate(i.ProductId, i.Quantity, i.PriceAtAdd, i.AddedAt)),
                    current.MergedGuestTokenHashes);
                next.Touch(current.UpdatedAt);
                next.MarkPersisted();
                next = Basket.Rehydrate(
                    next.Id,
                    next.UserId,
                    next.GuestTokenHash,
                    _expiredCreatedAt,
                    next.UpdatedAt,
                    next.Revision,
                    next.Items.Select(i => BasketItem.Rehydrate(i.ProductId, i.Quantity, i.PriceAtAdd, i.AddedAt)),
                    next.MergedGuestTokenHashes);
                _inner.Replace(key, next);
                return Task.FromResult(false); // CAS mismatch for observed revision
            }

            return Task.FromResult(_inner.TryDelete(key, expectedRevision));
        }

        private static BasketKey KeyOf(Basket basket) =>
            basket.UserId is { } userId ? BasketKey.ForUser(userId) : BasketKey.ForGuest(basket.GuestTokenHash!);
    }

    private sealed class FakeBasketStore : IBasketStore
    {
        private readonly InMemoryDictionaryBasketStore _inner;

        public FakeBasketStore(params Basket[] baskets)
        {
            _inner = new InMemoryDictionaryBasketStore(baskets);
        }

        public Task<Basket?> GetAsync(BasketKey key, CancellationToken ct = default) => _inner.GetAsync(key, ct);

        public Task<BasketSaveResult> SaveAsync(Basket basket, TimeSpan ttl, CancellationToken ct = default) =>
            _inner.SaveAsync(basket, ttl, ct);

        public Task<bool> DeleteAsync(BasketKey key, long expectedRevision, CancellationToken ct = default) =>
            _inner.DeleteAsync(key, expectedRevision, ct);
    }

    private sealed class ConflictingOnceBasketStore : IBasketStore
    {
        private readonly InMemoryDictionaryBasketStore _inner = new();
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

    private sealed class GuestMutationDuringMergeStore : IBasketStore
    {
        private readonly InMemoryDictionaryBasketStore _inner = new();
        private readonly BasketKey _guestKey;
        private readonly Guid _newProductId;
        private bool _mutated;

        public GuestMutationDuringMergeStore(BasketKey guestKey, Basket guest, Guid newProductId)
        {
            _guestKey = guestKey;
            _newProductId = newProductId;
            _inner.Replace(guestKey, guest);
        }

        public void Seed(BasketKey key, Basket basket) => _inner.Replace(key, basket);

        public Basket? GetCurrent(BasketKey key) => _inner.GetCurrent(key);

        public Task<Basket?> GetAsync(BasketKey key, CancellationToken ct = default) => _inner.GetAsync(key, ct);

        public Task<BasketSaveResult> SaveAsync(Basket basket, TimeSpan ttl, CancellationToken ct = default) =>
            _inner.SaveAsync(basket, ttl, ct);

        public Task<bool> DeleteAsync(BasketKey key, long expectedRevision, CancellationToken ct = default)
        {
            if (!_mutated && key.Value == _guestKey.Value)
            {
                var current = _inner.GetCurrent(key);
                if (current is not null && current.Revision == expectedRevision)
                {
                    // Simulate guest modified after read/merge save but before delete.
                    var updated = Basket.Rehydrate(
                        current.Id,
                        current.UserId,
                        current.GuestTokenHash,
                        current.CreatedAt,
                        Now.UtcDateTime,
                        current.Revision + 1,
                        current.Items
                            .Select(i => BasketItem.Rehydrate(i.ProductId, i.Quantity, i.PriceAtAdd, i.AddedAt))
                            .Concat([BasketItem.Rehydrate(_newProductId, 1, 30m, Now.UtcDateTime)]),
                        current.MergedGuestTokenHashes);
                    _inner.Replace(key, updated);
                    _mutated = true;
                    return Task.FromResult(false); // CAS conflict
                }
            }

            return _inner.DeleteAsync(key, expectedRevision, ct);
        }
    }

    private sealed class CleanupFailsOnceStore : IBasketStore
    {
        private readonly InMemoryDictionaryBasketStore _inner = new();
        private readonly BasketKey _guestKey;
        private bool _failed;

        public CleanupFailsOnceStore(BasketKey guestKey, Basket guest)
        {
            _guestKey = guestKey;
            _inner.Replace(guestKey, guest);
        }

        public Basket? GetCurrent(BasketKey key) => _inner.GetCurrent(key);

        public Task<Basket?> GetAsync(BasketKey key, CancellationToken ct = default) => _inner.GetAsync(key, ct);

        public Task<BasketSaveResult> SaveAsync(Basket basket, TimeSpan ttl, CancellationToken ct = default) =>
            _inner.SaveAsync(basket, ttl, ct);

        public Task<bool> DeleteAsync(BasketKey key, long expectedRevision, CancellationToken ct = default)
        {
            if (!_failed && key.Value == _guestKey.Value)
            {
                _failed = true;
                throw new BasketStorageException("transient cleanup failure");
            }

            return _inner.DeleteAsync(key, expectedRevision, ct);
        }
    }
}

