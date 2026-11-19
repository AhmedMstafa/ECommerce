using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Common.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ECommerce.IntegrationTests;

/// <summary>
/// Boots the real API against the throwaway SQL Server database. External
/// side effects (SMTP, Google certificate HTTP calls, Hangfire workers) are
/// removed so tests exercise application and persistence code, not the network.
/// </summary>
public sealed class TestingWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _originalEnvironment;

    public TestingWebApplicationFactory(string connectionString)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings__DefaultConnection"] = connectionString,
            ["Jwt__SecretKey"] = $"integration-tests-only-{Guid.NewGuid():N}",
            ["Swagger__Enabled"] = "true",
            ["ClientApp__BaseUrl"] = "https://client.example.com",
            ["RateLimiting__Fixed__PermitLimit"] = "10000",
            ["RateLimiting__Auth__PermitLimit"] = "10000"
        };

        // Program.cs captures several settings into locals while registering
        // services, before WebApplicationFactory's ConfigureAppConfiguration
        // callbacks can affect IConfiguration. Environment variables are loaded
        // by WebApplication.CreateBuilder itself, so they override those locals.
        _originalEnvironment = settings.Keys.ToDictionary(
            key => key,
            Environment.GetEnvironmentVariable);

        foreach (var (key, value) in settings)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService, RecordingEmailService>();

            services.RemoveAll<IGoogleTokenValidator>();
            services.AddSingleton<IGoogleTokenValidator, StubGoogleTokenValidator>();

            // AddInfrastructure registers a Hangfire BackgroundJobServerHostedService.
            // Tests must not have a background worker touching the test database.
            var hangfireHostedServices = services
                .Where(descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                                     IsHangfireHostedService(descriptor))
                .ToList();

            foreach (var descriptor in hangfireHostedServices)
            {
                services.Remove(descriptor);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var (key, value) in _originalEnvironment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        base.Dispose(disposing);
    }

    private static bool IsHangfireHostedService(ServiceDescriptor descriptor)
    {
        var implementationName = descriptor.ImplementationType?.FullName
                                 ?? descriptor.ImplementationInstance?.GetType().FullName;

        if (implementationName is not null)
        {
            return implementationName.StartsWith("Hangfire.", StringComparison.Ordinal);
        }

        return descriptor.ImplementationFactory?.Method.ReturnType.FullName?
                   .StartsWith("Hangfire.", StringComparison.Ordinal) == true;
    }
}

/// <summary>
/// Records direct send attempts. Password reset emails should be queued in the
/// outbox, not sent from the request pipeline, so direct sends stay at zero.
/// </summary>
public sealed class RecordingEmailService : IEmailService
{
    private int _sendCount;

    public int SendCount => Volatile.Read(ref _sendCount);

    public Task<Result> SendEmailAsync(
        string to,
        string subject,
        string body,
        bool isHtml = true,
        CancellationToken ct = default,
        string? messageId = null)
    {
        Interlocked.Increment(ref _sendCount);
        return Task.FromResult(Result.Success());
    }
}

/// <summary>
/// Deterministic Google identity stub. Distinct tokens cover successful login,
/// explicit-link mismatch, and an unlinked email owner without network calls.
/// </summary>
public sealed class StubGoogleTokenValidator : IGoogleTokenValidator
{
    public const string NewUserToken = "google-new-user-token";
    public const string ExistingEmailToken = "google-existing-email-token";
    public const string InvalidToken = "google-invalid-token";

    public const string NewUserEmail = "google-new-user@example.com";
    public const string NewUserSubject = "google-subject-new-user";

    public Task<Result<GoogleIdentity>> ValidateAsync(
        string idToken,
        CancellationToken ct = default)
    {
        var identity = idToken switch
        {
            NewUserToken => new GoogleIdentity(
                NewUserSubject,
                NewUserEmail,
                EmailVerified: true,
                FirstName: "Google",
                LastName: "User",
                Picture: null),
            ExistingEmailToken => new GoogleIdentity(
                "google-subject-existing-email",
                IntegrationTestFixture.AdminEmail,
                EmailVerified: true,
                FirstName: "System",
                LastName: "Administrator",
                Picture: null),
            _ => null
        };

        return Task.FromResult(identity is null
            ? Result.Failure<GoogleIdentity>(Error.Validation(
                "Google.InvalidToken",
                "The Google ID token is invalid."))
            : Result.Success(identity));
    }
}
