using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace ECommerce.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class PlatformIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task HealthLive_ShouldReturnHealthyEnvelope()
    {
        var response = await fixture.Client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/health+json");
        response.Headers.CacheControl?.NoStore.Should().BeTrue();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task HealthReady_ShouldIncludeSelfAndSqlServerChecks()
    {
        var response = await fixture.Client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entries = document.RootElement.GetProperty("entries");

        entries.GetProperty("self").GetProperty("status").GetString().Should().Be("Healthy");
        entries.GetProperty("sqlserver").GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task Swagger_ShouldExposeVersionedDocumentWithBearerScheme()
    {
        var response = await fixture.Client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        root.GetProperty("info").GetProperty("version").GetString().Should().Be("1.0");

        root.GetProperty("paths").EnumerateObject()
            .Should()
            .Contain(path => path.Name.Equals("/api/v1/products", StringComparison.OrdinalIgnoreCase));

        root.GetProperty("components")
            .GetProperty("securitySchemes")
            .TryGetProperty("Bearer", out _)
            .Should()
            .BeTrue();
    }
}
