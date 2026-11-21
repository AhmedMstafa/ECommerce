using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ECommerce.API.Filters;

/// <summary>
/// Global MVC action filter that logs action execution start and completion with
/// structured, non-sensitive context.
/// </summary>
public sealed class ActionLoggingFilter(ILogger<ActionLoggingFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (ShouldSkip(context))
        {
            await next();
            return;
        }

        var (controller, action) = GetActionNames(context);
        var httpMethod = context.HttpContext.Request.Method;
        var routeTemplate = GetRouteTemplate(context);
        var traceIdentifier = context.HttpContext.TraceIdentifier;
        var userId = GetUserId(context.HttpContext.User);

        logger.LogInformation(
            "Action {Controller}.{Action} started ({HttpMethod} {RouteTemplate}, TraceId: {TraceIdentifier}, UserId: {UserId})",
            controller,
            action,
            httpMethod,
            routeTemplate,
            traceIdentifier,
            userId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var executed = await next();
            stopwatch.Stop();

            var statusCode = GetStatusCode(executed);
            if (executed.Exception is { } exception && !executed.ExceptionHandled)
            {
                logger.LogError(
                    exception,
                    "Action {Controller}.{Action} failed with status {StatusCode} in {ElapsedMs} ms ({HttpMethod} {RouteTemplate}, TraceId: {TraceIdentifier}, UserId: {UserId})",
                    controller,
                    action,
                    statusCode,
                    stopwatch.Elapsed.TotalMilliseconds,
                    httpMethod,
                    routeTemplate,
                    traceIdentifier,
                    userId);
                return;
            }

            logger.LogInformation(
                "Action {Controller}.{Action} completed with status {StatusCode} in {ElapsedMs} ms ({HttpMethod} {RouteTemplate}, TraceId: {TraceIdentifier}, UserId: {UserId})",
                controller,
                action,
                statusCode,
                stopwatch.Elapsed.TotalMilliseconds,
                httpMethod,
                routeTemplate,
                traceIdentifier,
                userId);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            logger.LogInformation(
                "Action {Controller}.{Action} canceled after {ElapsedMs} ms ({HttpMethod} {RouteTemplate}, TraceId: {TraceIdentifier}, UserId: {UserId})",
                controller,
                action,
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
                "Action {Controller}.{Action} threw after {ElapsedMs} ms ({HttpMethod} {RouteTemplate}, TraceId: {TraceIdentifier}, UserId: {UserId})",
                controller,
                action,
                stopwatch.Elapsed.TotalMilliseconds,
                httpMethod,
                routeTemplate,
                traceIdentifier,
                userId);
            throw;
        }
    }

    private static bool ShouldSkip(ActionExecutingContext context)
    {
        var endpoint = context.HttpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<SkipActionLoggingAttribute>() is not null)
        {
            return true;
        }

        return context.ActionDescriptor.EndpointMetadata.OfType<SkipActionLoggingAttribute>().Any();
    }

    private static (string Controller, string Action) GetActionNames(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor cad)
        {
            return (cad.ControllerName, cad.ActionName);
        }

        return (context.ActionDescriptor.RouteValues.TryGetValue("controller", out var c) ? c ?? "unknown" : "unknown",
            context.ActionDescriptor.RouteValues.TryGetValue("action", out var a) ? a ?? "unknown" : "unknown");
    }

    private static string? GetRouteTemplate(ActionExecutingContext context)
    {
        var endpoint = context.HttpContext.GetEndpoint();
        var routeEndpoint = endpoint as RouteEndpoint;
        if (!string.IsNullOrWhiteSpace(routeEndpoint?.RoutePattern.RawText))
        {
            return routeEndpoint.RoutePattern.RawText;
        }

        if (context.ActionDescriptor.AttributeRouteInfo?.Template is { Length: > 0 } template)
        {
            return template;
        }

        return context.ActionDescriptor.DisplayName;
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

    private static int GetStatusCode(ActionExecutedContext context)
    {
        if (context.Exception is not null && !context.ExceptionHandled)
        {
            return context.HttpContext.Response.StatusCode;
        }

        return context.Result switch
        {
            ObjectResult objectResult => objectResult.StatusCode ?? context.HttpContext.Response.StatusCode,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            JsonResult jsonResult => jsonResult.StatusCode ?? context.HttpContext.Response.StatusCode,
            ContentResult contentResult => contentResult.StatusCode ?? context.HttpContext.Response.StatusCode,
            _ => context.HttpContext.Response.StatusCode
        };
    }
}
