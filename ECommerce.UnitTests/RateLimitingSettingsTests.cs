using ECommerce.API.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace ECommerce.UnitTests;

public class RateLimitingSettingsTests
{
    [Fact]
    public void Defaults_ShouldPreserveExistingLimits()
    {
        var settings = new RateLimitingSettings();

        settings.Fixed.PermitLimit.Should().Be(100);
        settings.Fixed.WindowSeconds.Should().Be(60);
        settings.Fixed.QueueLimit.Should().Be(10);
        settings.Auth.PermitLimit.Should().Be(5);
        settings.Auth.QueueLimit.Should().Be(0);
        settings.Basket.PermitLimit.Should().Be(60);
        settings.Basket.WindowSeconds.Should().Be(60);
        settings.Basket.QueueLimit.Should().Be(0);
    }

    [Fact]
    public void Settings_ShouldBindFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Fixed:PermitLimit"] = "25",
                ["RateLimiting:Fixed:WindowSeconds"] = "30",
                ["RateLimiting:Auth:PermitLimit"] = "2",
                ["RateLimiting:Basket:PermitLimit"] = "15",
                ["RateLimiting:Basket:WindowSeconds"] = "30"
            })
            .Build();

        var settings = configuration.GetSection("RateLimiting").Get<RateLimitingSettings>()!;

        settings.Fixed.PermitLimit.Should().Be(25);
        settings.Fixed.WindowSeconds.Should().Be(30);
        settings.Auth.PermitLimit.Should().Be(2);
        settings.Basket.PermitLimit.Should().Be(15);
        settings.Basket.WindowSeconds.Should().Be(30);
    }
}
