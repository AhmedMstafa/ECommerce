using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ECommerce.API.Health;

/// <summary>
/// Emits a stable JSON shape for both liveness and readiness probes while
/// preserving the conventional 200/503 status codes used by orchestrators.
/// </summary>
public static class HealthCheckResponseWriter
{
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/health+json";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.StatusCode = report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            entries = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2)
                })
        };

        await context.Response.WriteAsJsonAsync(
            payload,
            options: null,
            contentType: "application/health+json",
            cancellationToken: context.RequestAborted);
    }
}
