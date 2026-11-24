using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.BackgroundJobs;

public sealed class RecurringJobRegistrationService(
    IRecurringJobManager recurringJobManager,
    IRecurringJobRegistrationState registrationState,
    ILogger<RecurringJobRegistrationService> logger,
    TimeSpan? initialDelay = null,
    TimeSpan? maxDelay = null) : BackgroundService
{
    internal static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _initialDelay = initialDelay ?? DefaultInitialDelay;
    private readonly TimeSpan _maxDelay = maxDelay ?? DefaultMaxDelay;

    public const string OutboxJobId = "process-outbox-messages";
    public const string ReservationExpirationJobId = "expire-stock-reservations";
    public const string OutboxCron = "*/15 * * * * *";
    public const string ReservationExpirationCron = "* * * * *";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;
        var delay = _initialDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            attempt++;
            var attemptedAtUtc = DateTimeOffset.UtcNow;
            registrationState.ReportAttempt(attemptedAtUtc);

            try
            {
                logger.LogInformation(
                    "Registering Hangfire recurring jobs. Attempt {Attempt}.",
                    attempt);

                RegisterRecurringJobs();

                registrationState.ReportSuccess(DateTimeOffset.UtcNow);
                logger.LogInformation(
                    "Hangfire recurring jobs registered successfully on attempt {Attempt}. JobIds: {JobIds}",
                    attempt,
                    new[] { OutboxJobId, ReservationExpirationJobId });

                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Hangfire recurring job registration cancelled during shutdown. Attempt {Attempt}.",
                    attempt);
                return;
            }
            catch (Exception ex)
            {
                registrationState.ReportFailure(ex.Message, DateTimeOffset.UtcNow);
                logger.LogError(
                    ex,
                    "Failed to register Hangfire recurring jobs on attempt {Attempt}. Retrying in {RetryDelay}.",
                    attempt,
                    delay);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    logger.LogInformation(
                        "Hangfire recurring job registration cancelled while waiting to retry. Attempt {Attempt}.",
                        attempt);
                    return;
                }

                delay = NextDelay(delay);
            }
        }
    }

    public void RegisterRecurringJobs()
    {
        recurringJobManager.AddOrUpdate<OutboxProcessorJob>(
            OutboxJobId,
            job => job.ProcessPendingMessagesAsync(null),
            OutboxCron);

        recurringJobManager.AddOrUpdate<ReservationExpirationJob>(
            ReservationExpirationJobId,
            job => job.ExpireReservationsAsync(CancellationToken.None),
            ReservationExpirationCron);
    }

    private TimeSpan NextDelay(TimeSpan current)
    {
        var nextTicks = current.Ticks * 2;
        var maxTicks = _maxDelay.Ticks;
        return TimeSpan.FromTicks(Math.Min(nextTicks, maxTicks));
    }
}
