using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ECommerce.UseCases.Common.Behaviors;

public class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int SlowThresholdMilliseconds = 500;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        var elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        if (elapsedMilliseconds > SlowThresholdMilliseconds)
        {
            var requestName = typeof(TRequest).Name;
            logger.LogWarning("[LONG RUNNING] Request {RequestName} took {ElapsedMilliseconds}ms (threshold: {Threshold}ms) {@Request}",
                requestName, elapsedMilliseconds, SlowThresholdMilliseconds, request);
        }

        return response;
    }
}
