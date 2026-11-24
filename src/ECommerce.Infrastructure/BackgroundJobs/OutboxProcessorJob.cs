using ECommerce.Domain.Entities;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerce.Infrastructure.BackgroundJobs;

public class OutboxProcessorJob(
    IOutboxMessageStore store,
    IServiceProvider serviceProvider,
    IOutboxClock clock,
    IOptions<OutboxSettings> options,
    ILogger<OutboxProcessorJob> logger)
{
    private readonly OutboxSettings _settings = options.Value;

    [Queue("outbox")]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 5, 30, 120 })]
    [DisableConcurrentExecution(60)]
    public async Task ProcessPendingMessagesAsync(
        IJobCancellationToken? jobCancellationToken,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            jobCancellationToken?.ShutdownToken ?? CancellationToken.None);

        var jobToken = linked.Token;
        jobCancellationToken?.ThrowIfCancellationRequested();
        jobToken.ThrowIfCancellationRequested();

        var leaseOwner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
        var claimed = await store.ClaimAsync(
            new OutboxClaimRequest(
                _settings.BatchSize,
                leaseOwner,
                _settings.LeaseDuration,
                clock.UtcNow),
            jobToken);

        if (claimed.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "Claimed {Count} outbox messages under lease {LeaseOwner}",
            claimed.Count,
            leaseOwner);

        foreach (var message in claimed)
        {
            jobToken.ThrowIfCancellationRequested();

            var attempt = message.RetryCount + 1;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(jobToken);
            timeout.CancelAfter(_settings.HandlerTimeout);

            try
            {
                // The message itself is passed to the handler so handlers can use
                // its stable idempotency key (for example as a MIME Message-Id).
                var handler = serviceProvider.GetKeyedService<IOutboxMessageHandler>(message.Type);
                if (handler is null)
                {
                    throw new InvalidOperationException(
                        $"No outbox handler is registered for message type '{message.Type}'.");
                }

                logger.LogInformation(
                    "Handling outbox message {Id} of type {Type}, attempt {Attempt}",
                    message.Id,
                    message.Type,
                    attempt);

                await handler.HandleAsync(message, timeout.Token);

                message.MarkAsProcessed(clock.UtcNow);
                logger.LogInformation("Outbox message {Id} processed", message.Id);
            }
            catch (OperationCanceledException) when (jobToken.IsCancellationRequested)
            {
                // Hangfire is shutting down or cancellation was requested. Release
                // the lease immediately instead of waiting for it to expire.
                message.ReleaseLease(clock.UtcNow);
                logger.LogWarning(
                    "Outbox message {Id} lease released because the job was cancelled",
                    message.Id);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                RecordFailure(
                    message,
                    attempt,
                    $"Handler timed out after {_settings.HandlerTimeoutSeconds} seconds.");
                logger.LogWarning(
                    "Outbox message {Id} timed out after {TimeoutSeconds}s",
                    message.Id,
                    _settings.HandlerTimeoutSeconds);
            }
            catch (Exception ex)
            {
                RecordFailure(message, attempt, ex.Message);
                logger.LogError(
                    ex,
                    "Failed to process outbox message {Id} on attempt {Attempt}",
                    message.Id,
                    attempt);
            }

            // Persist after each message so a shutdown cannot lose completed work.
            await store.SaveChangesAsync(jobToken.IsCancellationRequested ? CancellationToken.None : jobToken);
        }

        jobToken.ThrowIfCancellationRequested();
    }

    private void RecordFailure(OutboxMessage message, int attempt, string error)
    {
        if (attempt >= _settings.MaxAttempts)
        {
            message.MarkAsDeadLettered(error);
            logger.LogError(
                "Outbox message {Id} moved to Failed after {Attempt} attempts",
                message.Id,
                attempt);
            return;
        }

        var delay = OutboxBackoff.Calculate(attempt, _settings.BaseDelay, _settings.MaxDelay);
        message.MarkAsFailed(error, clock.UtcNow.Add(delay));
    }
}
