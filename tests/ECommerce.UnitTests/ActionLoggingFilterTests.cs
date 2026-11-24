using System.Security.Claims;
using ECommerce.API.Filters;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ECommerce.UnitTests;

public class ActionLoggingFilterTests
{
    [Fact]
    public async Task ShouldLogStartAndCompletion_WithExpectedProperties()
    {
        var logger = new TestLogger<ActionLoggingFilter>();
        var filter = new ActionLoggingFilter(logger);
        var (executing, executed) = CreateContexts(new ControllerActionDescriptor
        {
            ControllerName = "Products",
            ActionName = "GetAll"
        }, userId: "user-123", statusCode: StatusCodes.Status200OK);

        await filter.OnActionExecutionAsync(executing, () => Task.FromResult(executed));

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("started", StringComparison.OrdinalIgnoreCase) &&
            e.GetValue("Controller") as string == "Products" &&
            e.GetValue("Action") as string == "GetAll" &&
            e.GetValue("HttpMethod") as string == "GET" &&
            e.GetValue("UserId") as string == "user-123");

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("completed", StringComparison.OrdinalIgnoreCase) &&
            Equals(e.GetValue("StatusCode"), StatusCodes.Status200OK) &&
            e.GetValue("ElapsedMs") is double);
    }

    [Fact]
    public async Task ShouldSkipLogging_WhenActionHasSkipAttribute()
    {
        var logger = new TestLogger<ActionLoggingFilter>();
        var filter = new ActionLoggingFilter(logger);
        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = "Products",
            ActionName = "GetAll",
            EndpointMetadata = [new SkipActionLoggingAttribute()]
        };
        var (executing, executed) = CreateContexts(descriptor);

        await filter.OnActionExecutionAsync(executing, () => Task.FromResult(executed));

        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldSkipLogging_WhenControllerHasSkipAttribute()
    {
        var logger = new TestLogger<ActionLoggingFilter>();
        var filter = new ActionLoggingFilter(logger);
        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = "Products",
            ActionName = "GetAll",
            EndpointMetadata = [new SkipActionLoggingAttribute()]
        };
        var (executing, executed) = CreateContexts(descriptor);

        await filter.OnActionExecutionAsync(executing, () => Task.FromResult(executed));

        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldLogExceptionContext_AndRethrow()
    {
        var logger = new TestLogger<ActionLoggingFilter>();
        var filter = new ActionLoggingFilter(logger);
        var (executing, _) = CreateContexts(new ControllerActionDescriptor
        {
            ControllerName = "Basket",
            ActionName = "Get"
        }, statusCode: StatusCodes.Status500InternalServerError);

        var act = () => filter.OnActionExecutionAsync(executing, () => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error &&
            e.Exception is InvalidOperationException &&
            e.GetValue("Controller") as string == "Basket");
    }

    [Fact]
    public async Task ShouldNotLogSensitiveValues()
    {
        var logger = new TestLogger<ActionLoggingFilter>();
        var filter = new ActionLoggingFilter(logger);
        var (executing, executed) = CreateContexts(new ControllerActionDescriptor
        {
            ControllerName = "Auth",
            ActionName = "Login"
        });
        executing.HttpContext.Request.Headers.Authorization = "Bearer secret-token";
        executing.HttpContext.Request.Headers.Cookie = "guest_basket_token=secret";

        await filter.OnActionExecutionAsync(executing, () => Task.FromResult(executed));

        var all = string.Join(" | ", logger.Entries.Select(e => e.Message + " " + string.Join(",", e.State.Select(kv => $"{kv.Key}={kv.Value}"))));
        all.Should().NotContain("secret-token");
        all.Should().NotContain("guest_basket_token=secret");
        all.Should().NotContain("Authorization");
    }

    private static (ActionExecutingContext Executing, ActionExecutedContext Executed) CreateContexts(
        ControllerActionDescriptor descriptor,
        string? userId = null,
        int statusCode = StatusCodes.Status200OK)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "trace-1";
        httpContext.Request.Method = "GET";
        httpContext.Response.StatusCode = statusCode;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId)
            ], "test"));
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        var executing = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());

        var executed = new ActionExecutedContext(
            actionContext,
            new List<IFilterMetadata>(),
            controller: new object())
        {
            Result = new OkResult()
        };
        executed.HttpContext.Response.StatusCode = statusCode;

        return (executing, executed);
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>>
                ?? [];
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception, values.ToDictionary(k => k.Key, v => v.Value)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> State)
    {
        public object? GetValue(string key) => State.TryGetValue(key, out var value) ? value : null;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
