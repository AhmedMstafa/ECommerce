using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ECommerce.UseCases.Common.Behaviors;

public class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation("[START] Handling command/query {RequestName} {@Request}", requestName, request);

        try
        {
            var response = await next();
            stopwatch.Stop();
            logger.LogInformation("[END] Handled {RequestName} successfully in {ElapsedMilliseconds}ms", 
                requestName, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "[ERROR] Handling {RequestName} failed after {ElapsedMilliseconds}ms", 
                requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
