using ECommerce.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ECommerce.UnitTests;

public class RecurringJobRegistrationServiceTests
{
    [Fact]
    public void RegisterRecurringJobs_ShouldPreserveIdsAndSchedules()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        var state = new RecurringJobRegistrationState();
        var service = new RecurringJobRegistrationService(
            manager,
            state,
            NullLogger<RecurringJobRegistrationService>.Instance);

        service.RegisterRecurringJobs();

        manager.Received(1).AddOrUpdate(
            RecurringJobRegistrationService.OutboxJobId,
            Arg.Is<Job>(job => job.Type == typeof(OutboxProcessorJob)
                               && job.Method.Name == nameof(OutboxProcessorJob.ProcessPendingMessagesAsync)),
            RecurringJobRegistrationService.OutboxCron,
            Arg.Any<RecurringJobOptions>());

        manager.Received(1).AddOrUpdate(
            RecurringJobRegistrationService.ReservationExpirationJobId,
            Arg.Is<Job>(job => job.Type == typeof(ReservationExpirationJob)
                               && job.Method.Name == nameof(ReservationExpirationJob.ExpireReservationsAsync)),
            RecurringJobRegistrationService.ReservationExpirationCron,
            Arg.Any<RecurringJobOptions>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportSuccess_WhenRegistrationSucceeds()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        var state = new RecurringJobRegistrationState();
        var service = new RecurringJobRegistrationService(
            manager,
            state,
            NullLogger<RecurringJobRegistrationService>.Instance,
            initialDelay: TimeSpan.FromMilliseconds(10),
            maxDelay: TimeSpan.FromMilliseconds(20));

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => state.IsRegistered, TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        state.IsRegistered.Should().BeTrue();
        state.LastSuccessUtc.Should().NotBeNull();
        state.LastError.Should().BeNull();
        manager.Received(1).AddOrUpdate(
            RecurringJobRegistrationService.OutboxJobId,
            Arg.Is<Job>(job => job.Type == typeof(OutboxProcessorJob)),
            RecurringJobRegistrationService.OutboxCron,
            Arg.Any<RecurringJobOptions>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRetryAfterFailure_AndSucceed()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        var state = new RecurringJobRegistrationState();
        var registrationAttempts = 0;

        manager.When(m => m.AddOrUpdate(
                Arg.Any<string>(),
                Arg.Any<Job>(),
                Arg.Any<string>(),
                Arg.Any<RecurringJobOptions>()))
            .Do(callInfo =>
            {
                if (callInfo.ArgAt<string>(0) == RecurringJobRegistrationService.OutboxJobId)
                {
                    var currentAttempt = Interlocked.Increment(ref registrationAttempts);
                    if (currentAttempt == 1)
                        throw new InvalidOperationException("SQL unavailable");
                }
            });

        var service = new RecurringJobRegistrationService(
            manager,
            state,
            NullLogger<RecurringJobRegistrationService>.Instance,
            initialDelay: TimeSpan.FromMilliseconds(10),
            maxDelay: TimeSpan.FromMilliseconds(20));

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => state.IsRegistered, TimeSpan.FromSeconds(3));
        await service.StopAsync(CancellationToken.None);

        state.IsRegistered.Should().BeTrue();
        state.LastError.Should().BeNull();
        registrationAttempts.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldStopRetrying_WhenCancelled()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        var state = new RecurringJobRegistrationState();
        manager.When(m => m.AddOrUpdate(
                Arg.Any<string>(),
                Arg.Any<Job>(),
                Arg.Any<string>(),
                Arg.Any<RecurringJobOptions>()))
            .Do(_ => throw new InvalidOperationException("SQL unavailable"));

        using var cts = new CancellationTokenSource();
        var service = new RecurringJobRegistrationService(
            manager,
            state,
            NullLogger<RecurringJobRegistrationService>.Instance,
            initialDelay: TimeSpan.FromMilliseconds(50),
            maxDelay: TimeSpan.FromMilliseconds(50));

        await service.StartAsync(cts.Token);
        await WaitForAsync(() => state.LastAttemptUtc.HasValue, TimeSpan.FromSeconds(2));
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        state.IsRegistered.Should().BeFalse();
        state.LastError.Should().Contain("SQL unavailable");
    }

    [Fact]
    public void AddInfrastructure_ShouldRegisterRecurringJobRegistrationService_OnlyOutsideTesting()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=.;Database=Test;Trusted_Connection=True;TrustServerCertificate=True",
            ["Jwt:SecretKey"] = "test-secret-key-minimum-32-bytes-long",
            ["Redis:Configuration"] = "localhost:6379,abortConnect=false",
            ["Stripe:SecretKey"] = "sk_test_dummy",
            ["Stripe:WebhookSecret"] = "whsec_dummy"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var testingServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        ECommerce.Infrastructure.DependancyInjection.AddInfrastructure(testingServices, configuration, isDevelopment: false, isTesting: true);
        testingServices.Any(d => d.ImplementationType == typeof(RecurringJobRegistrationService))
            .Should().BeFalse("testing environment should not register recurring job registration hosted service");

        var productionServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        ECommerce.Infrastructure.DependancyInjection.AddInfrastructure(productionServices, configuration, isDevelopment: false, isTesting: false);
        productionServices.Any(d => d.ImplementationType == typeof(RecurringJobRegistrationService))
            .Should().BeTrue("non-testing environment should register recurring job registration hosted service");
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (condition()) return;
            await Task.Delay(20);
        }

        condition().Should().BeTrue("condition should become true within timeout");
    }
}
