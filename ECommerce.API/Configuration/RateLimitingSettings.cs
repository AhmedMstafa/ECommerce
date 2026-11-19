namespace ECommerce.API.Configuration;

/// <summary>
/// Configuration for the two in-memory rate-limit policies. Limits are applied
/// per client key (authenticated user name, otherwise remote IP address).
/// </summary>
public sealed class RateLimitingSettings
{
    public RateLimitPolicySettings Fixed { get; set; } = new()
    {
        PermitLimit = 100,
        WindowSeconds = 60,
        QueueLimit = 10
    };

    public RateLimitPolicySettings Auth { get; set; } = new()
    {
        PermitLimit = 5,
        WindowSeconds = 60,
        QueueLimit = 0
    };
}

public sealed class RateLimitPolicySettings
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
    public int QueueLimit { get; set; }
}
