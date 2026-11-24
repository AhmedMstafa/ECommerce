using ECommerce.Infrastructure;
using ECommerce.Infrastructure.Payments;
using ECommerce.UseCases.Common.Payments;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerce.UnitTests;

public class StripeConfigurationTests
{
    [Fact]
    public void StripeSettings_ShouldBind_FromConfigurationSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:SecretKey"] = "sk_test_example",
                ["Stripe:PublishableKey"] = "pk_test_example",
                ["Stripe:WebhookSecret"] = "whsec_example"
            })
            .Build();

        var settings = configuration.GetSection(StripeSettings.SectionName).Get<StripeSettings>();

        settings.Should().NotBeNull();
        settings!.SecretKey.Should().Be("sk_test_example");
        settings.PublishableKey.Should().Be("pk_test_example");
        settings.WebhookSecret.Should().Be("whsec_example");
    }

    [Fact]
    public void StripeSettings_ShouldBind_FromEnvironmentVariableStyleKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe__SecretKey"] = "sk_test_env",
                ["Stripe__PublishableKey"] = "pk_test_env",
                ["Stripe__WebhookSecret"] = "whsec_env"
            })
            .Build();

        // Environment variables provider maps __ to : in real app config.
        // Simulate the effective mapped keys as they appear after env provider.
        var mapped = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:SecretKey"] = configuration["Stripe__SecretKey"],
                ["Stripe:PublishableKey"] = configuration["Stripe__PublishableKey"],
                ["Stripe:WebhookSecret"] = configuration["Stripe__WebhookSecret"]
            })
            .Build();

        var settings = mapped.GetSection(StripeSettings.SectionName).Get<StripeSettings>();

        settings.Should().NotBeNull();
        settings!.SecretKey.Should().Be("sk_test_env");
        settings.PublishableKey.Should().Be("pk_test_env");
        settings.WebhookSecret.Should().Be("whsec_env");
    }

    [Fact]
    public void AddInfrastructure_ShouldFailFast_WhenStripeSecretKeyMissing()
    {
        var configuration = BuildBaseConfiguration(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "",
            ["Stripe:WebhookSecret"] = "whsec_example"
        });

        var services = new ServiceCollection();

        var act = () => services.AddInfrastructure(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Stripe:SecretKey is not configured.");
    }

    [Fact]
    public void AddInfrastructure_ShouldFailFast_WhenStripeWebhookSecretMissing()
    {
        var configuration = BuildBaseConfiguration(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_test_example",
            ["Stripe:WebhookSecret"] = ""
        });

        var services = new ServiceCollection();

        var act = () => services.AddInfrastructure(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Stripe:WebhookSecret is not configured.");
    }

    [Fact]
    public void AddInfrastructure_ShouldBypassStripeValidation_InTestingEnvironment()
    {
        var configuration = BuildBaseConfiguration(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "",
            ["Stripe:WebhookSecret"] = ""
        });

        var services = new ServiceCollection();

        var act = () => services.AddInfrastructure(configuration, isTesting: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void StripePaymentService_ShouldBeConstructible_WithConfiguredSettings()
    {
        var settings = Options.Create(new StripeSettings
        {
            SecretKey = "sk_test_example",
            PublishableKey = "pk_test_example",
            WebhookSecret = "whsec_example"
        });

        var service = new StripePaymentService(settings, NullLogger<StripePaymentService>.Instance);

        service.Should().NotBeNull();
    }

    [Fact]
    public void VerifyWebhook_ShouldFailSafely_WithInvalidSignature_WithoutExposingSecret()
    {
        var settings = Options.Create(new StripeSettings
        {
            SecretKey = "sk_test_example",
            PublishableKey = "pk_test_example",
            WebhookSecret = "whsec_example"
        });

        var service = new StripePaymentService(settings, NullLogger<StripePaymentService>.Instance);

        var result = service.VerifyWebhook("{}", "t=0,v1=invalid");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Payment.InvalidWebhookSignature");
        result.Error.Message.Should().NotContain("whsec_example");
        result.Error.Message.Should().NotContain("sk_test_example");
    }

    private static IConfiguration BuildBaseConfiguration(Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\MSSQLLocalDB;Database=ECommerceTest;Trusted_Connection=True;",
            ["Jwt:SecretKey"] = "unit-test-only-key-change-me-0123456789",
            ["Jwt:Issuer"] = "ECommerceApi",
            ["Jwt:Audience"] = "ECommerceClient",
            ["Redis:Configuration"] = "localhost:6379,abortConnect=false",
            ["Redis:InstanceName"] = "ECommerceTest:"
        };

        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
