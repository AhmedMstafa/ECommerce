namespace ECommerce.API.Configuration;

/// <summary>
/// Configuration for in-memory rate-limit policies. Limits are applied per
/// partition key (authenticated identity or a safe guest/client key).
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

    /// <summary>
    /// Protects temporary basket operations. Partitioned by authenticated user
    /// identity when available, otherwise by a safe guest/client key.
    /// </summary>
    public RateLimitPolicySettings Basket { get; set; } = new()
    {
        PermitLimit = 60,
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
