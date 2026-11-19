using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ECommerce.UnitTests;

public class OutboxProcessorTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ProcessPendingMessagesAsync_ShouldMarkClaimedMessageProcessed()
    {
        var message = CreateClaimedMessage();
        var store = CreateStore(message);
        var handler = Substitute.For<IOutboxMessageHandler>();
        handler.HandleAsync(message, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var provider = CreateProvider(handler);
        var job = CreateJob(store, provider, CreateSettings());

        await job.ProcessPendingMessagesAsync(null);

        message.Status.Should().Be(OutboxMessageStatus.Processed);
        message.ProcessedOnUtc.Should().Be(Now);
        message.ErrorMessage.Should().BeNull();
        await handler.Received(1).HandleAsync(message, Arg.Any<CancellationToken>());
        await store.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_ShouldDeadLetterUnknownMessageType()
    {
        var message = CreateClaimedMessage();
        var store = CreateStore(message);
        var settings = CreateSettings();
        settings.MaxAttempts = 1;
        var job = CreateJob(store, CreateProvider(null), settings);

        await job.ProcessPendingMessagesAsync(null);

        message.Status.Should().Be(OutboxMessageStatus.Failed);
        message.ErrorMessage.Should().Contain("No outbox handler is registered");
        await store.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_ShouldApplyBackoffAfterTransientFailure()
    {
        var message = CreateClaimedMessage();
        var store = CreateStore(message);
        var handler = Substitute.For<IOutboxMessageHandler>();
        handler.HandleAsync(message, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("SMTP down")));
        var job = CreateJob(store, CreateProvider(handler), CreateSettings());

        await job.ProcessPendingMessagesAsync(null);

        message.Status.Should().Be(OutboxMessageStatus.Pending);
        message.RetryCount.Should().Be(1);
        message.ErrorMessage.Should().Be("SMTP down");
        message.NextAttemptAtUtc.Should().Be(Now.AddSeconds(5));
        message.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_ShouldDeadLetterAfterRetryBudget()
    {
        var message = CreateClaimedMessage();
        message.MarkAsFailed("previous failure", Now.AddMinutes(-1));
        message.MarkAsProcessing("lease-owner", TimeSpan.FromMinutes(1), Now);
        var store = CreateStore(message);
        var handler = Substitute.For<IOutboxMessageHandler>();
        handler.HandleAsync(message, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("permanent failure")));
        var settings = CreateSettings();
        settings.MaxAttempts = 2;
        var job = CreateJob(store, CreateProvider(handler), settings);

        await job.ProcessPendingMessagesAsync(null);

        message.Status.Should().Be(OutboxMessageStatus.Failed);
        message.RetryCount.Should().Be(2);
        message.NextAttemptAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_ShouldReleaseLeaseAndPropagateShutdownCancellation()
    {
        var message = CreateClaimedMessage();
        var store = CreateStore(message);
        using var outerCancellation = new CancellationTokenSource();
        var handler = Substitute.For<IOutboxMessageHandler>();
        handler.HandleAsync(message, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            outerCancellation.Cancel();
            return Task.FromException(new OperationCanceledException());
        });
        var job = CreateJob(store, CreateProvider(handler), CreateSettings());

        var act = () => job.ProcessPendingMessagesAsync(null, outerCancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        message.Status.Should().Be(OutboxMessageStatus.Pending);
        message.LeaseOwner.Should().BeNull();
        await store.Received(1).SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_ShouldTreatHandlerTimeoutAsFailure()
    {
        var message = CreateClaimedMessage();
        var store = CreateStore(message);
        var handler = Substitute.For<IOutboxMessageHandler>();
        handler.HandleAsync(message, Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.Delay(Timeout.Infinite, callInfo.ArgAt<CancellationToken>(1)));
        var job = CreateJob(store, CreateProvider(handler), CreateSettings());

        await job.ProcessPendingMessagesAsync(null);

        message.Status.Should().Be(OutboxMessageStatus.Pending);
        message.ErrorMessage.Should().Contain("timed out");
        message.NextAttemptAtUtc.Should().Be(Now.AddSeconds(5));
    }

    private static OutboxMessage CreateClaimedMessage()
    {
        var message = OutboxMessage.Create("Email", "{\"to\":\"user@example.com\"}").Value!;
        message.MarkAsProcessing("lease-owner", TimeSpan.FromMinutes(1), Now);
        return message;
    }

    private static IOutboxMessageStore CreateStore(OutboxMessage message)
    {
        var store = Substitute.For<IOutboxMessageStore>();
        store.ClaimAsync(Arg.Any<OutboxClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutboxMessage>>(new[] { message }));
        store.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        return store;
    }

    private static IServiceProvider CreateProvider(IOutboxMessageHandler? handler)
    {
        var services = new ServiceCollection();

        if (handler is not null)
        {
            services.AddKeyedSingleton(typeof(IOutboxMessageHandler), "Email", handler);
        }

        // A real provider is required because IKeyedServiceProvider cannot be
        // expressed by a plain IServiceProvider substitute.
        return services.BuildServiceProvider();
    }

    private static OutboxSettings CreateSettings() => new()
    {
        BatchSize = 20,
        MaxAttempts = 3,
        LeaseDurationSeconds = 60,
        HandlerTimeoutSeconds = 1,
        BaseDelaySeconds = 5,
        MaxDelaySeconds = 300
    };

    private static OutboxProcessorJob CreateJob(
        IOutboxMessageStore store,
        IServiceProvider provider,
        OutboxSettings settings)
    {
        return new OutboxProcessorJob(
            store,
            provider,
            new FixedClock(Now),
            Options.Create(settings),
            NullLogger<OutboxProcessorJob>.Instance);
    }

    private sealed class FixedClock(DateTime utcNow) : IOutboxClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
