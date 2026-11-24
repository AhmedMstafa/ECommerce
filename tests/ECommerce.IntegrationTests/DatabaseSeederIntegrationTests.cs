using ECommerce.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DatabaseSeedingTestCollection
{
    public const string Name = "Database seeding tests";
}

[Collection(DatabaseSeedingTestCollection.Name)]
public sealed class DatabaseSeederIntegrationTests : IAsyncLifetime
{
    private SqlServerTestDatabase _database = null!;
    private TestingWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _database = await SqlServerTestDatabase.CreateAsync();
        _factory = CreateFactory();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task DevelopmentStartup_ShouldSeedMissingDataOnce()
    {
        await AssertSeededDataAsync();

        // Restarting the host against the same database must not duplicate rows.
        _client.Dispose();
        _factory.Dispose();

        _factory = CreateFactory();
        _client = _factory.CreateClient();

        await AssertSeededDataAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _database.DisposeAsync();
    }

    private TestingWebApplicationFactory CreateFactory() =>
        new(_database.ConnectionString, "Development", settings =>
        {
            settings["SeedAdmin__Email"] = "seedtest.admin@example.com";
            settings["SeedAdmin__Password"] = "SeedTest123!";
            settings["SeedDemoUsers__Enabled"] = "true";
            settings["SeedDemoUsers__Password"] = "SeedTest123!";
            settings["Stripe__SecretKey"] = "sk_test_seed_integration";
            settings["Stripe__WebhookSecret"] = "whsec_seed_integration";
        });

    private async Task AssertSeededDataAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        (await db.Brands.CountAsync()).Should().Be(5);
        (await db.Types.CountAsync()).Should().Be(5);
        (await db.DeliveryMethods.CountAsync()).Should().Be(3);
        (await db.Products.CountAsync()).Should().Be(11);
        (await db.Users.CountAsync()).Should().Be(3);
        (await db.Addresses.CountAsync()).Should().Be(3);
        (await db.Addresses.CountAsync(x => x.IsDefault)).Should().Be(2);
        (await db.Users.AnyAsync(x => x.Email == "demo.customer@example.com")).Should().BeTrue();
        (await db.Users.AnyAsync(x => x.Email == "demo.shopper@example.com")).Should().BeTrue();
    }
}
