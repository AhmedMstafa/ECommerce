using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.Infrastructure.Persistence;
using ECommerce.UseCases.Baskets.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ECommerce.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class BasketIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task Get_ShouldCreateGuestBasket_AndReturnGuestToken()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/basket");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("X-Guest-Token").Should().BeTrue();
        response.Headers.GetValues("Set-Cookie").Should().Contain(cookie => cookie.Contains("guest_basket_token="));

        var body = await response.Content.ReadFromJsonAsync<TestApiResponse<BasketResponse>>();
        body!.Data!.Items.Should().BeEmpty();
        body.Data.UserId.Should().BeNull();
    }

    [Fact]
    public async Task AddGetUpdateRemoveClear_ShouldWorkForGuestBasket()
    {
        using var client = CreateClient();

        var add = await PostAsJsonAsync(client, "/api/v1/basket/items", new
        {
            productId = fixture.FirstProductId,
            quantity = 2
        });
        add.StatusCode.Should().Be(HttpStatusCode.OK);
        var guestToken = add.Headers.GetValues("X-Guest-Token").Single();

        var addAgain = await PostAsJsonAsync(client, "/api/v1/basket/items", new
        {
            productId = fixture.FirstProductId,
            quantity = 1
        }, guestToken);
        var addAgainBody = await addAgain.Content.ReadFromJsonAsync<TestApiResponse<BasketResponse>>();
        addAgainBody!.Data!.Items.Should().ContainSingle().Which.Quantity.Should().Be(3);

        var update = await PutAsJsonAsync(client, $"/api/v1/basket/items/{fixture.FirstProductId}", new
        {
            quantity = 2
        }, guestToken);
        var updateBody = await update.Content.ReadFromJsonAsync<TestApiResponse<BasketResponse>>();
        updateBody!.Data!.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);

        var remove = await DeleteAsync(client, $"/api/v1/basket/items/{fixture.FirstProductId}", guestToken);
        var removeBody = await remove.Content.ReadFromJsonAsync<TestApiResponse<BasketResponse>>();
        removeBody!.Data!.Items.Should().BeEmpty();

        await PostAsJsonAsync(client, "/api/v1/basket/items", new
        {
            productId = fixture.SecondProductId,
            quantity = 1
        }, guestToken);
        var clear = await DeleteAsync(client, "/api/v1/basket", guestToken);
        var clearBody = await clear.Content.ReadFromJsonAsync<TestApiResponse<BasketResponse>>();
        clearBody!.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GuestBaskets_ShouldBeIsolated()
    {
        using var clientA = CreateClient();
        using var clientB = CreateClient();

        var a = await PostAsJsonAsync(clientA, "/api/v1/basket/items", new
        {
            productId = fixture.FirstProductId,
            quantity = 1
        });
        var tokenA = a.Headers.GetValues("X-Guest-Token").Single();

        var b = await PostAsJsonAsync(clientB, "/api/v1/basket/items", new
        {
            productId = fixture.SecondProductId,
            quantity = 2
        });
        var tokenB = b.Headers.GetValues("X-Guest-Token").Single();

        var aBody = await (await GetAsync(clientA, "/api/v1/basket", tokenA)).Content
            .ReadFromJsonAsync<TestApiResponse<BasketResponse>>();
        var bBody = await (await GetAsync(clientB, "/api/v1/basket", tokenB)).Content
            .ReadFromJsonAsync<TestApiResponse<BasketResponse>>();

        aBody!.Data!.Items.Should().ContainSingle().Which.ProductId.Should().Be(fixture.FirstProductId);
        bBody!.Data!.Items.Should().ContainSingle().Which.ProductId.Should().Be(fixture.SecondProductId);
        tokenA.Should().NotBe(tokenB);
    }

    [Fact]
    public async Task AuthenticatedUser_ShouldAccessOwnBasket()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminToken);

        var add = await client.PostAsJsonAsync("/api/v1/basket/items", new
        {
            productId = fixture.FirstProductId,
            quantity = 1
        });
        add.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await add.Content.ReadFromJsonAsync<TestApiResponse<BasketResponse>>();
        body!.Data!.UserId.Should().Be(fixture.AdminUserId);
        body.Data.Items.Should().ContainSingle();

        await client.DeleteAsync("/api/v1/basket");
    }

    [Fact]
    public async Task MergeEndpoint_ShouldMergeGuestBasketIntoAuthenticatedBasket()
    {
        using var client = CreateClient();

        var guestAdd = await PostAsJsonAsync(client, "/api/v1/basket/items", new
        {
            productId = fixture.FirstProductId,
            quantity = 2
        });
        var guestToken = guestAdd.Headers.GetValues("X-Guest-Token").Single();

        // Prepare authenticated basket.
        await PostAsJsonAsync(client, "/api/v1/basket/items", new
        {
            productId = fixture.FirstProductId,
            quantity = 1
        }, authToken: fixture.AdminToken);
        await PostAsJsonAsync(client, "/api/v1/basket/items", new
        {
            productId = fixture.ThirdProductId,
            quantity = 3
        }, authToken: fixture.AdminToken);

        using var mergeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/basket/merge");
        mergeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminToken);
        mergeRequest.Headers.Add("X-Guest-Token", guestToken);

        var merge = await client.SendAsync(mergeRequest);
        merge.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await merge.Content.ReadFromJsonAsync<TestApiResponse<BasketResponse>>();
        body!.Data!.Items.Single(i => i.ProductId == fixture.FirstProductId).Quantity.Should().Be(3);
        body.Data.Items.Single(i => i.ProductId == fixture.ThirdProductId).Quantity.Should().Be(3);

        await DeleteAsync(client, "/api/v1/basket", authToken: fixture.AdminToken);
    }

    [Fact]
    public async Task Login_ShouldInlineMergeGuestBasket()
    {
        var email = $"merge-{Guid.NewGuid():N}@example.com";
        const string password = "StrongPass1!";

        using var client = CreateClient();
        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password,
            firstName = "Merge",
            lastName = "User"
        });

        var guestAdd = await PostAsJsonAsync(client, "/api/v1/basket/items", new
        {
            productId = fixture.FirstProductId,
            quantity = 2
        });
        var guestToken = guestAdd.Headers.GetValues("X-Guest-Token").Single();

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        loginRequest.Headers.Add("X-Guest-Token", guestToken);
        var login = await client.SendAsync(loginRequest);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBody = await login.Content.ReadFromJsonAsync<TestApiResponse<AuthTokens>>();
        var token = loginBody!.Data!.Token;

        var basket = await (await GetAsync(client, "/api/v1/basket", authToken: token)).Content
            .ReadFromJsonAsync<TestApiResponse<BasketResponse>>();
        basket!.Data!.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);

        await DeleteAsync(client, "/api/v1/basket", authToken: token);
    }

    [Fact]
    public async Task Get_ShouldReflectCurrentPriceChanges()
    {
        using var client = CreateClient();
        var add = await PostAsJsonAsync(client, "/api/v1/basket/items", new
        {
            productId = fixture.FirstProductId,
            quantity = 2
        });
        var guestToken = add.Headers.GetValues("X-Guest-Token").Single();

        await SetProductPriceAsync(fixture.FirstProductId, 99m);
        try
        {
            var body = await (await GetAsync(client, "/api/v1/basket", guestToken)).Content
                .ReadFromJsonAsync<TestApiResponse<BasketResponse>>();
            var item = body!.Data!.Items.Single();
            item.CurrentUnitPrice.Should().Be(99m);
            item.PriceAtAdd.Should().Be(10m);
            item.HasPriceChanged.Should().BeTrue();
            item.PriceDifference.Should().Be(89m);
            item.LineTotal.Should().Be(198m);
        }
        finally
        {
            await SetProductPriceAsync(fixture.FirstProductId, 10m);
            await DeleteAsync(client, "/api/v1/basket", guestToken);
        }
    }

    [Fact]
    public async Task Get_ShouldMarkOutOfStockAndQuantityReduced()
    {
        using var client = CreateClient();
        var add = await PostAsJsonAsync(client, "/api/v1/basket/items", new
        {
            productId = fixture.ThirdProductId,
            quantity = 4
        });
        var guestToken = add.Headers.GetValues("X-Guest-Token").Single();

        await SetProductStockAsync(fixture.ThirdProductId, 2);
        try
        {
            var reduced = await (await GetAsync(client, "/api/v1/basket", guestToken)).Content
                .ReadFromJsonAsync<TestApiResponse<BasketResponse>>();
            var item = reduced!.Data!.Items.Single();
            item.Status.Should().Be("QuantityReduced");
            item.AvailableQuantity.Should().Be(2);
            item.LineTotal.Should().Be(60m); // 2 * 30

            await SetProductStockAsync(fixture.ThirdProductId, 0);
            var outOfStock = await (await GetAsync(client, "/api/v1/basket", guestToken)).Content
                .ReadFromJsonAsync<TestApiResponse<BasketResponse>>();
            var outItem = outOfStock!.Data!.Items.Single();
            outItem.Status.Should().Be("OutOfStock");
            outItem.LineTotal.Should().Be(0m);
            outOfStock.Data.HasUnavailableItems.Should().BeTrue();
        }
        finally
        {
            await SetProductStockAsync(fixture.ThirdProductId, 5);
            await DeleteAsync(client, "/api/v1/basket", guestToken);
        }
    }

    [Fact]
    public async Task AddItem_ShouldRejectQuantityAboveStock()
    {
        using var client = CreateClient();

        var response = await PostAsJsonAsync(client, "/api/v1/basket/items", new
        {
            productId = fixture.ThirdProductId, // stock 5
            quantity = 6
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ConcurrentGuestUpdates_ShouldNotLoseItems()
    {
        using var client = CreateClient();
        var seed = await client.GetAsync("/api/v1/basket");
        var token = seed.Headers.GetValues("X-Guest-Token").Single();

        var tasks = Enumerable.Range(0, 8).Select(i =>
            PostAsJsonAsync(client, "/api/v1/basket/items", new
            {
                productId = i % 2 == 0 ? fixture.FirstProductId : fixture.SecondProductId,
                quantity = 1
            }, token));

        await Task.WhenAll(tasks);

        var body = await (await GetAsync(client, "/api/v1/basket", token)).Content
            .ReadFromJsonAsync<TestApiResponse<BasketResponse>>();
        body!.Data!.TotalItems.Should().Be(8);

        await DeleteAsync(client, "/api/v1/basket", token);
    }

    [Fact]
    public async Task StorageFailure_ShouldReturnStructuredError()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBasketStore>();
                services.AddSingleton<IBasketStore, ThrowingBasketStore>();
            });
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/basket");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("title").GetString().Should().Be("Server Error");
        document.RootElement.GetProperty("detail").GetString().Should().Contain("temporarily unavailable");
    }

    private HttpClient CreateClient() => fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false
    });

    private static Task<HttpResponseMessage> GetAsync(
        HttpClient client, string url, string? guestToken = null, string? authToken = null) =>
        SendAsync(client, HttpMethod.Get, url, null, guestToken, authToken);

    private static Task<HttpResponseMessage> DeleteAsync(
        HttpClient client, string url, string? guestToken = null, string? authToken = null) =>
        SendAsync(client, HttpMethod.Delete, url, null, guestToken, authToken);

    private static Task<HttpResponseMessage> PostAsJsonAsync(
        HttpClient client, string url, object body, string? guestToken = null, string? authToken = null) =>
        SendAsync(client, HttpMethod.Post, url, JsonContent.Create(body), guestToken, authToken);

    private static Task<HttpResponseMessage> PutAsJsonAsync(
        HttpClient client, string url, object body, string? guestToken = null, string? authToken = null) =>
        SendAsync(client, HttpMethod.Put, url, JsonContent.Create(body), guestToken, authToken);

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        HttpContent? content,
        string? guestToken,
        string? authToken)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (!string.IsNullOrWhiteSpace(guestToken))
        {
            request.Headers.Add("X-Guest-Token", guestToken);
        }

        if (!string.IsNullOrWhiteSpace(authToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        }

        return client.SendAsync(request);
    }

    private async Task SetProductPriceAsync(Guid productId, decimal price)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var product = await db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == productId);
        product.UpdatePrice(price);
        await db.SaveChangesAsync();
    }

    private async Task SetProductStockAsync(Guid productId, int stockQuantity)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var product = await db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == productId);
        product.UpdateStock(stockQuantity);
        await db.SaveChangesAsync();
    }

    private sealed record AuthTokens(string Token, string Email, string FullName, DateTime ExpiresAt);

    private sealed class ThrowingBasketStore : IBasketStore
    {
        public Task<Basket?> GetAsync(BasketKey key, CancellationToken ct = default) =>
            throw new BasketStorageException("Redis unavailable");

        public Task<BasketSaveResult> SaveAsync(Basket basket, TimeSpan ttl, CancellationToken ct = default) =>
            throw new BasketStorageException("Redis unavailable");

        public Task<bool> DeleteAsync(BasketKey key, long expectedRevision, CancellationToken ct = default) =>
            throw new BasketStorageException("Redis unavailable");
    }
}
