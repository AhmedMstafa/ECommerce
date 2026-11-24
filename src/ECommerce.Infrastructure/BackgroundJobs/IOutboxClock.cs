namespace ECommerce.Infrastructure.BackgroundJobs;

/// <summary>
/// Small abstraction so retry and lease calculations are testable without
/// waiting on the system clock.
/// </summary>
public interface IOutboxClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemOutboxClock : IOutboxClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
