using System.Text;
using ECommerce.API.Health;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ECommerce.UnitTests;

public class HealthCheckResponseWriterTests
{
    [Fact]
    public async Task WriteAsync_ShouldReturnJsonAnd200_WhenHealthy()
    {
        var context = CreateContext();
        var report = CreateReport(HealthStatus.Healthy);

        await HealthCheckResponseWriter.WriteAsync(context, report);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.ContentType.Should().Be("application/health+json");
        context.Response.Headers.CacheControl.ToString().Should().Be("no-store");

        var body = await ReadBodyAsync(context);
        body.Should().Contain("\"status\":\"Healthy\"");
        body.Should().Contain("\"self\"");
        body.Should().Contain("\"database\"");
    }

    [Fact]
    public async Task WriteAsync_ShouldReturn503_WhenUnhealthy()
    {
        var context = CreateContext();

        await HealthCheckResponseWriter.WriteAsync(context, CreateReport(HealthStatus.Unhealthy));

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    private static DefaultHttpContext CreateContext()
    {
        return new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() }
        };
    }

    private static HealthReport CreateReport(HealthStatus status)
    {
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["self"] = new(status, "API process is running", TimeSpan.FromMilliseconds(2), null, null),
            ["database"] = new(status, "Database check", TimeSpan.FromMilliseconds(8), null, null)
        };

        return new HealthReport(entries, TimeSpan.FromMilliseconds(10));
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
