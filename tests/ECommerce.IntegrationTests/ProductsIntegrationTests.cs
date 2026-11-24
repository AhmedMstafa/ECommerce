using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ECommerce.UseCases.Products.Commands.CreateProduct;
using ECommerce.UseCases.Products.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace ECommerce.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class ProductsIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task GetAll_ShouldReturnPagedEnvelope_WithStableMetadata()
    {
        var response = await fixture.Client.GetAsync(
            "/api/v1/products?pageNumber=2&pageSize=2&sortBy=name&sortOrder=asc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content
            .ReadFromJsonAsync<TestApiResponse<IReadOnlyList<GetAllProductsResponse>>>();

        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.StatusCode.Should().Be(StatusCodes.Status200OK);
        body.Meta.Should().NotBeNull();
        body.Meta!.TraceId.Should().NotBeNullOrWhiteSpace();
        body.Meta.PageNumber.Should().Be(2);
        body.Meta.PageSize.Should().Be(2);
        body.Meta.TotalCount.Should().Be(3);
        body.Meta.TotalPages.Should().Be(2);
        body.Meta.HasPreviousPage.Should().BeTrue();
        body.Meta.HasNextPage.Should().BeFalse();
        body.Data.Should().ContainSingle()
            .Which.Name.Should().Be("Gamma Product");
    }

    [Fact]
    public async Task GetAll_ShouldApplySearchFiltersAndSortingTogether()
    {
        var response = await fixture.Client.GetAsync(
            $"/api/v1/products?search=gamma&brandId={fixture.BrandId}&minPrice=10&maxPrice=40&sortBy=price&sortOrder=desc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content
            .ReadFromJsonAsync<TestApiResponse<IReadOnlyList<GetAllProductsResponse>>>();

        body!.Data.Should().ContainSingle()
            .Which.Name.Should().Be("Gamma Product");
        body.Meta!.TotalCount.Should().Be(1);
    }

    [Theory]
    [InlineData("pageNumber=0", "Page number")]
    [InlineData("pageSize=51", "Page size")]
    [InlineData("sort=unknown", "Sort")]
    [InlineData("sortBy=passwordHash", "SortBy")]
    [InlineData("sortOrder=sideways", "SortOrder")]
    [InlineData("minPrice=-1", "MinPrice")]
    [InlineData("minPrice=100&maxPrice=10", "MinPrice cannot")]
    public async Task GetAll_ShouldReturnValidationProblem_WhenInputIsInvalid(
        string query,
        string expectedMessage)
    {
        var response = await fixture.Client.GetAsync($"/api/v1/products?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");

        errors.EnumerateObject()
            .SelectMany(property => property.Value.EnumerateArray())
            .Select(value => value.GetString())
            .Should()
            .Contain(message => message != null && message.Contains(expectedMessage));
    }

    [Fact]
    public async Task GetById_ShouldReturnNotFoundProblem_WhenProductDoesNotExist()
    {
        var response = await fixture.Client.GetAsync($"/api/v1/products/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("title").GetString().Should().Be("Not Found");
    }

    [Fact]
    public async Task Create_ShouldRejectAnonymousRequest()
    {
        using var request = CreateProductRequest();

        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_ShouldPersistProduct_WhenAdminIsAuthenticated()
    {
        var productName = $"Created Product {Guid.NewGuid():N}";
        using var request = CreateProductRequest(productName, fixture.AdminToken);

        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var body = await response.Content.ReadFromJsonAsync<TestApiResponse<Guid>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.StatusCode.Should().Be(StatusCodes.Status201Created);

        var createdId = body.Data;
        response.Headers.Location!.ToString().Should().Contain(createdId.ToString());

        var getResponse = await fixture.Client.GetAsync($"/api/v1/products/{createdId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getBody = await getResponse.Content
            .ReadFromJsonAsync<TestApiResponse<GetByIdProductResponse>>();
        getBody!.Data!.Name.Should().Be(productName);
    }

    private HttpRequestMessage CreateProductRequest(
        string? name = null,
        string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products")
        {
            Content = JsonContent.Create(new CreateProductCommand(
                name ?? "Unauthorized Product",
                "Created by an integration test.",
                "https://images.test/created.png",
                42m,
                fixture.BrandId,
                fixture.TypeId))
        };

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }
}
