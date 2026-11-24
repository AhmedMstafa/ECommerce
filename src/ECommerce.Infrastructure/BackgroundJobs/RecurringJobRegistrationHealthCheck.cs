using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ECommerce.Infrastructure.BackgroundJobs;

public sealed class RecurringJobRegistrationHealthCheck(
    IRecurringJobRegistrationState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["registered"] = state.IsRegistered,
            ["lastAttemptUtc"] = state.LastAttemptUtc?.ToString("O") ?? "never",
            ["lastSuccessUtc"] = state.LastSuccessUtc?.ToString("O") ?? "never"
        };

        if (state.IsRegistered)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "Hangfire recurring jobs are registered.", data));
        }

        if (!string.IsNullOrWhiteSpace(state.LastError))
        {
            data["lastError"] = state.LastError!;
        }

        return Task.FromResult(HealthCheckResult.Unhealthy(
            "Hangfire recurring jobs are not yet registered. Registration will keep retrying.",
            data: data));
    }
}
