namespace ECommerce.Infrastructure.BackgroundJobs;

public sealed class OutboxSettings
{
    public const string SectionName = "Outbox";

    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 5;
    public int LeaseDurationSeconds { get; set; } = 60;
    public int HandlerTimeoutSeconds { get; set; } = 30;
    public int BaseDelaySeconds { get; set; } = 5;
    public int MaxDelaySeconds { get; set; } = 300;

    public TimeSpan LeaseDuration => TimeSpan.FromSeconds(LeaseDurationSeconds);
    public TimeSpan HandlerTimeout => TimeSpan.FromSeconds(HandlerTimeoutSeconds);
    public TimeSpan BaseDelay => TimeSpan.FromSeconds(BaseDelaySeconds);
    public TimeSpan MaxDelay => TimeSpan.FromSeconds(MaxDelaySeconds);

    public void Validate()
    {
        if (BatchSize is < 1 or > 500)
            throw new InvalidOperationException("Outbox:BatchSize must be between 1 and 500.");
        if (MaxAttempts is < 1 or > 20)
            throw new InvalidOperationException("Outbox:MaxAttempts must be between 1 and 20.");
        if (LeaseDurationSeconds < 5)
            throw new InvalidOperationException("Outbox:LeaseDurationSeconds must be at least 5.");
        if (HandlerTimeoutSeconds < 1 || HandlerTimeoutSeconds > LeaseDurationSeconds)
            throw new InvalidOperationException(
                "Outbox:HandlerTimeoutSeconds must be at least 1 and no greater than LeaseDurationSeconds.");
        if (BaseDelaySeconds < 0 || MaxDelaySeconds < BaseDelaySeconds)
            throw new InvalidOperationException(
                "Outbox:MaxDelaySeconds must not be less than Outbox:BaseDelaySeconds.");
    }
}
