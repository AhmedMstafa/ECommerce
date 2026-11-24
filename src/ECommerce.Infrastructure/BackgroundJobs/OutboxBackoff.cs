namespace ECommerce.Infrastructure.BackgroundJobs;

/// <summary>
/// Deterministic exponential backoff. Determinism makes retry behavior easy to
/// test and explain; the lease prevents concurrent workers from racing while a
/// message is being handled.
/// </summary>
public static class OutboxBackoff
{
    public static TimeSpan Calculate(int failedAttempt, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        if (failedAttempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(failedAttempt), "Attempt number starts at 1.");
        }

        if (baseDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (maxDelay < baseDelay)
        {
            maxDelay = baseDelay;
        }

        var delay = baseDelay;
        for (var attempt = 1; attempt < failedAttempt && delay < maxDelay; attempt++)
        {
            var nextTicks = delay.Ticks > maxDelay.Ticks / 2
                ? maxDelay.Ticks
                : delay.Ticks * 2;

            delay = TimeSpan.FromTicks(Math.Min(nextTicks, maxDelay.Ticks));
        }

        return delay;
    }
}
