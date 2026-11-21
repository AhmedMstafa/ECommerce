using ECommerce.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ECommerce.UnitTests;

public class RedisRegistrationTests
{
    [Fact]
    public async Task AddInfrastructure_ShouldShareOneMultiplexerWithRedisCache()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=localhost;Database=ECommerceRegistrationTest;Integrated Security=True;TrustServerCertificate=True",
                ["Jwt:SecretKey"] = "registration-test-only-secret-0123456789",
                ["Redis:Configuration"] = "localhost:6379,abortConnect=false,connectTimeout=1000",
                ["Redis:InstanceName"] = "ECommerceTest:"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var root = provider.GetRequiredService<IConnectionMultiplexer>();
        var scoped = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var options = provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value;

        scoped.Should().BeSameAs(root);
        options.ConnectionMultiplexerFactory.Should().NotBeNull();
        (await options.ConnectionMultiplexerFactory!()).Should().BeSameAs(root);
        options.InstanceName.Should().Be("ECommerceTest:");
    }
}
