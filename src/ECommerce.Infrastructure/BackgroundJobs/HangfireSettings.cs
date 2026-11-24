namespace ECommerce.Infrastructure.BackgroundJobs;

public sealed class HangfireSettings
{
    public const string SectionName = "Hangfire";

    public int WorkerCount { get; set; } = Math.Max(2, Environment.ProcessorCount);
    public int QueuePollIntervalSeconds { get; set; } = 15;
    public int SlidingInvisibilityTimeoutMinutes { get; set; } = 5;
    public int CommandBatchMaxTimeoutMinutes { get; set; } = 5;
    public int ServerTimeoutSeconds { get; set; } = 60;

    public void Validate()
    {
        if (WorkerCount < 2 || WorkerCount > 64)
            throw new InvalidOperationException("Hangfire:WorkerCount must be between 2 and 64.");
        if (QueuePollIntervalSeconds < 1)
            throw new InvalidOperationException("Hangfire:QueuePollIntervalSeconds must be at least 1.");
        if (SlidingInvisibilityTimeoutMinutes < 1)
            throw new InvalidOperationException("Hangfire:SlidingInvisibilityTimeoutMinutes must be at least 1.");
        if (CommandBatchMaxTimeoutMinutes < 1)
            throw new InvalidOperationException("Hangfire:CommandBatchMaxTimeoutMinutes must be at least 1.");
        if (ServerTimeoutSeconds < 10)
            throw new InvalidOperationException("Hangfire:ServerTimeoutSeconds must be at least 10.");
    }
}
