using ECommerce.Infrastructure.Caching;
using FluentAssertions;

namespace ECommerce.UnitTests;

public class RedisSettingsTests
{
    [Fact]
    public void ToConfigurationOptions_ShouldEnableTls_ForUpstashRedissUrl()
    {
        var settings = new RedisSettings
        {
            Configuration = "rediss://default:s3cret@localhost:6379"
        };

        var options = settings.ToConfigurationOptions();

        options.Ssl.Should().BeTrue();
        options.Password.Should().Be("s3cret");
        options.EndPoints.Should().HaveCount(1);
        options.EndPoints.Single().ToString().Should().Contain("localhost").And.Contain("6379");
        options.AbortOnConnectFail.Should().BeFalse();
    }

    [Fact]
    public void ToConfigurationOptions_ShouldDecodeEscapedPassword_AndDefaultPort()
    {
        var settings = new RedisSettings
        {
            Configuration = "rediss://default:p%40ss%3Aword@localhost"
        };

        var options = settings.ToConfigurationOptions();

        options.Password.Should().Be("p@ss:word");
        options.EndPoints.Single().ToString().Should().Contain("6379");
    }

    [Fact]
    public void ToConfigurationOptions_ShouldAcceptClassicConnectionString()
    {
        var settings = new RedisSettings
        {
            Configuration = "localhost:6380,password=secret,ssl=True"
        };

        var options = settings.ToConfigurationOptions();

        options.Ssl.Should().BeTrue();
        options.Password.Should().Be("secret");
        options.EndPoints.Single().ToString().Should().Contain("6380");
        options.AbortOnConnectFail.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldThrow_WhenConfigurationMissing()
    {
        var settings = new RedisSettings();

        var act = () => settings.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Redis:Configuration*")
            .WithMessage("*Redis__Configuration*");
    }

    [Fact]
    public void Validate_ShouldThrow_WhenConfigurationIsPlaceholder_WithoutEchoingValue()
    {
        var settings = new RedisSettings
        {
            Configuration = "replace-with-upstash-rediss-connection-string"
        };

        var act = () => settings.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*placeholder*")
            .WithMessage("*Redis:Configuration*");
    }

    [Fact]
    public void Validate_ShouldThrow_WhenInstanceNameIsEmpty()
    {
        var settings = new RedisSettings
        {
            Configuration = "rediss://default:s3cret@localhost:6379",
            InstanceName = string.Empty
        };

        var act = () => settings.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Redis:InstanceName*");
    }
}
