using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Identity;
using ECommerce.Infrastructure.Persistence;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Common.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.IntegrationTests;

public static class IntegrationTestCollection
{
    public const string Name = "SQL Server integration tests";
}

[CollectionDefinition(IntegrationTestCollection.Name)]
public sealed class IntegrationTestCollectionDefinition
    : ICollectionFixture<IntegrationTestFixture>;

/// <summary>
/// One real database and one API host are shared by the integration suite.
/// Migrations run before any request, which is the main reason EF InMemory is
/// not acceptable here: it would hide SQL and migration regressions.
/// </summary>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@integration.test";

    private SqlServerTestDatabase? _database;
    private TestingWebApplicationFactory? _factory;

    private HttpClient? _client;

    public HttpClient Client =>
        _client ?? throw new InvalidOperationException("Fixture has not been initialized.");

    public TestingWebApplicationFactory Factory =>
        _factory ?? throw new InvalidOperationException("Fixture has not been initialized.");

    public RecordingEmailService EmailService { get; private set; } = null!;

    public string DatabaseProvider => _database?.Provider ?? "uninitialized";

    public Guid AdminUserId { get; private set; }

    public string AdminToken { get; private set; } = null!;

    public Guid BrandId { get; private set; }

    public Guid TypeId { get; private set; }

    public async Task InitializeAsync()
    {
        _database = await SqlServerTestDatabase.CreateAsync();
        _factory = new TestingWebApplicationFactory(_database.ConnectionString);

        // Creating the client starts the real host with the test configuration.
        _client = _factory.CreateClient();
        EmailService = (RecordingEmailService)_factory.Services.GetRequiredService<IEmailService>();

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        await dbContext.Database.MigrateAsync();

        await SeedCatalogAsync(dbContext);
        await SeedAdminAsync(scope.ServiceProvider);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();

        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private async Task SeedCatalogAsync(StoreDbContext dbContext)
    {
        var brand = ProductBrand.Create(Guid.NewGuid(), "Integration Brand").Value;
        var type = ProductType.Create(Guid.NewGuid(), "Integration Type").Value;

        var products = new[]
        {
            Product.Create("Alpha Product", "First seeded product", "https://images.test/alpha.png", 10m, brand.Id, type.Id).Value,
            Product.Create("Beta Product", "Second seeded product", "https://images.test/beta.png", 20m, brand.Id, type.Id).Value,
            Product.Create("Gamma Product", "Third seeded product", "https://images.test/gamma.png", 30m, brand.Id, type.Id).Value
        };

        dbContext.Brands.Add(brand);
        dbContext.Types.Add(type);
        dbContext.Products.AddRange(products);
        await dbContext.SaveChangesAsync();

        BrandId = brand.Id;
        TypeId = type.Id;
    }

    private async Task SeedAdminAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roleManager.RoleExistsAsync(AppRoles.Admin))
        {
            (await roleManager.CreateAsync(new IdentityRole<Guid>(AppRoles.Admin))).EnsureSucceeded();
        }

        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        var admin = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = AdminEmail,
            UserName = AdminEmail,
            EmailConfirmed = true,
            FirstName = "System",
            LastName = "Administrator"
        };

        // Generated per run and never exposed; it only satisfies Identity policy.
        var password = $"Aa1!{Guid.NewGuid():N}";
        (await userManager.CreateAsync(admin, password)).EnsureSucceeded();
        (await userManager.AddToRoleAsync(admin, AppRoles.Admin)).EnsureSucceeded();

        AdminUserId = admin.Id;

        var jwtProvider = services.GetRequiredService<IJwtProvider>();
        AdminToken = jwtProvider.GenerateToken(
            admin.Id,
            admin.Email!,
            "System Administrator",
            [AppRoles.Admin]);
    }
}

internal static class IdentityResultExtensions
{
    public static void EnsureSucceeded(this IdentityResult result)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Identity operation failed: {errors}");
        }
    }
}
