using ECommerce.Infrastructure;
using ECommerce.Infrastructure.Caching;
using ECommerce.UseCases.Common.Caching;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ECommerce.UnitTests;

public class RedisRegistrationTests
{
    [Fact]
    public async Task AddInfrastructure_ShouldRegisterSharedRedisMultiplexerAndHybridCache()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=localhost;Database=ECommerceRegistrationTest;Integrated Security=True;TrustServerCertificate=True",
                ["Jwt:SecretKey"] = "registration-test-only-secret-0123456789",
                ["Redis:Configuration"] = "localhost:6379,abortConnect=false,connectTimeout=1000",
                ["Redis:InstanceName"] = "ECommerceTest:",
                ["Cache:DefaultExpiration"] = "00:05:00",
                ["Cache:LocalCacheExpiration"] = "00:01:00",
                ["Cache:DistributedCacheExpiration"] = "00:05:00"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration, isTesting: true);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var root = provider.GetRequiredService<IConnectionMultiplexer>();
        var scoped = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var options = provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value;

        scoped.Should().BeSameAs(root);
        options.ConnectionMultiplexerFactory.Should().NotBeNull();
        (await options.ConnectionMultiplexerFactory!()).Should().BeSameAs(root);
        options.InstanceName.Should().Be("ECommerceTest:");

        provider.GetRequiredService<HybridCache>().Should().NotBeNull();
        provider.GetRequiredService<ICacheService>().Should().BeOfType<HybridCacheService>();
        provider.GetRequiredService<IDistributedCache>().Should().NotBeNull();
        provider.GetRequiredService<IOptions<CacheSettings>>().Value.LocalCacheExpiration
            .Should().Be(TimeSpan.FromMinutes(1));
    }
}
