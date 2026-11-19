using ECommerce.UseCases.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Middlewares;

public class GlobalExceptionMiddleware(
    IProblemDetailsService problemDetailsService, 
    ILogger<GlobalExceptionMiddleware> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, 
        Exception exception, 
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Exception occurred: {Message}", exception.Message);

        if (exception is ValidationException validationException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

            var problemDetailsContext = new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new HttpValidationProblemDetails(
                    validationException.Errors.ToDictionary(k => k.Key, v => v.Value))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Validation Failed",
                    Detail = "One or more validation errors occurred."
                }
            };

            return await problemDetailsService.TryWriteAsync(problemDetailsContext);
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var defaultProblemContext = new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred. Please try again later."
            }
        };

        return await problemDetailsService.TryWriteAsync(defaultProblemContext);
    }
}
