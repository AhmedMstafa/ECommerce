using StackExchange.Redis;

namespace ECommerce.Infrastructure.Caching;

/// <summary>
/// Redis/Upstash configuration. Values are supplied through the root .env file
/// in development and environment variables in deployment; never in source.
/// </summary>
public sealed class RedisSettings
{
    public const string SectionName = "Redis";

    /// <summary>
    /// Full Upstash connection string, e.g. rediss://default:PASSWORD@host:6379.
    /// </summary>
    public string Configuration { get; set; } = string.Empty;

    /// <summary>Prefix applied to every key by IDistributedCache.</summary>
    public string InstanceName { get; set; } = "ECommerce:";

    public void Validate()
    {
        // Messages name the missing key only; values are never logged or echoed.
        if (string.IsNullOrWhiteSpace(Configuration))
        {
            throw new InvalidOperationException(
                "Redis:Configuration is not configured. Set Redis__Configuration in the root .env file for development or as an environment variable in production.");
        }

        if (Configuration.Contains("replace-with", StringComparison.OrdinalIgnoreCase) ||
            Configuration.Contains("your-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Redis:Configuration still contains a placeholder value. Supply the real Upstash connection string.");
        }

        if (string.IsNullOrWhiteSpace(InstanceName))
        {
            throw new InvalidOperationException("Redis:InstanceName cannot be empty.");
        }
    }

    /// <summary>
    /// Accepts both the Upstash rediss:// URL form and the StackExchange.Redis
    /// "host:port,password=...,ssl=True" form. A single ConfigurationOptions is
    /// created here so the connection is shared for the process lifetime.
    /// </summary>
    public ConfigurationOptions ToConfigurationOptions()
    {
        ConfigurationOptions options;

        if (Uri.TryCreate(Configuration, UriKind.Absolute, out var uri) &&
            uri.Scheme is "redis" or "rediss")
        {
            options = new ConfigurationOptions
            {
                Ssl = string.Equals(uri.Scheme, "rediss", StringComparison.OrdinalIgnoreCase),
                AbortOnConnectFail = false,
                ConnectTimeout = 5000,
                SyncTimeout = 5000
            };

            options.EndPoints.Add(uri.Host, uri.Port > 0 ? uri.Port : 6379);

            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                var separator = uri.UserInfo.IndexOf(':');
                var encodedPassword = separator >= 0
                    ? uri.UserInfo[(separator + 1)..]
                    : uri.UserInfo;

                options.Password = Uri.UnescapeDataString(encodedPassword);
            }
        }
        else
        {
            options = ConfigurationOptions.Parse(Configuration);
        }

        // Redis is a cache: a transient outage must never prevent the API from
        // starting or serving data from SQL Server.
        options.AbortOnConnectFail = false;
        return options;
    }
}
