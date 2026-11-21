using System.Diagnostics;
using System.Security.Claims;

namespace ECommerce.API.Filters;

/// <summary>
/// Reusable endpoint filter for Minimal API endpoints. Applies the same
/// structured, non-sensitive logging principles as <see cref="ActionLoggingFilter"/>.
/// Register only on applicable endpoints, for example:
/// <c>app.MapGet("/example", ...).AddEndpointFilter&lt;EndpointLoggingFilter&gt;();</c>
/// </summary>
public sealed class EndpointLoggingFilter(ILogger<EndpointLoggingFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var endpoint = httpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<SkipActionLoggingAttribute>() is not null)
        {
            return await next(context);
        }

        var displayName = endpoint?.DisplayName ?? "endpoint";
        var httpMethod = httpContext.Request.Method;
        var routeTemplate = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? httpContext.Request.Path.Value;
        var traceIdentifier = httpContext.TraceIdentifier;
        var userId = GetUserId(httpContext.User);

        logger.LogInformation(
            "Endpoint {Endpoint} started ({HttpMethod} {RouteTemplate}, TraceId: {TraceIdentifier}, UserId: {UserId})",
            displayName,
            httpMethod,
            routeTemplate,
            traceIdentifier,
            userId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await next(context);
            stopwatch.Stop();

            logger.LogInformation(
                "Endpoint {Endpoint} completed with status {StatusCode} in {ElapsedMs} ms ({HttpMethod} {RouteTemplate}, TraceId: {TraceIdentifier}, UserId: {UserId})",
                displayName,
                httpContext.Response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds,
                httpMethod,
                routeTemplate,
                traceIdentifier,
                userId);

            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            logger.LogInformation(
                "Endpoint {Endpoint} canceled after {ElapsedMs} ms ({HttpMethod} {RouteTemplate}, TraceId: {TraceIdentifier}, UserId: {UserId})",
                displayName,
                stopwatch.Elapsed.TotalMilliseconds,
                httpMethod,
                routeTemplate,
                traceIdentifier,
                userId);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(
                ex,
                "Endpoint {Endpoint} threw after {ElapsedMs} ms ({HttpMethod} {RouteTemplate}, TraceId: {TraceIdentifier}, UserId: {UserId})",
                displayName,
                stopwatch.Elapsed.TotalMilliseconds,
                httpMethod,
                routeTemplate,
                traceIdentifier,
                userId);
            throw;
        }
    }

    private static string? GetUserId(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");
    }
}
