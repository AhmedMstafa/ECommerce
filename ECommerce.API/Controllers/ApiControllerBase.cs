using Asp.Versioning;
using ECommerce.API.Models;
using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Pagination;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Correlates the response with the Serilog CorrelationId. The fallback keeps
    /// controller unit tests working without a fully populated ControllerContext.
    /// </summary>
    protected string CurrentTraceId => HttpContext?.TraceIdentifier ?? string.Empty;

    protected ActionResult<ApiResponse<T>> FromResult<T>(Result<T> result)
        => FromResult(result, value => ApiResponse<T>.SuccessResponse(
            value,
            meta: ApiMeta.For(CurrentTraceId)));

    protected ActionResult<ApiResponse<IReadOnlyList<T>>> FromPagedResult<T>(Result<PagedResponse<T>> result)
        => FromResult(result, page => ApiResponse<IReadOnlyList<T>>.SuccessResponse(
            page.Items,
            meta: ApiMeta.ForPage(CurrentTraceId, page)));

    private ActionResult<ApiResponse<TResult>> FromResult<T, TResult>(
        Result<T> result,
        Func<T, ApiResponse<TResult>> onSuccess)
    {
        return result.Match<ActionResult<ApiResponse<TResult>>>(
            value => Ok(onSuccess(value)),
            error => error.Type switch
            {
                ErrorType.NotFound => NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Not Found",
                    Detail = error.Message
                }),
                ErrorType.Validation => BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Bad Request",
                    Detail = error.Message
                }),
                ErrorType.Conflict => Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Conflict",
                    Detail = error.Message
                }),
                ErrorType.Unauthorized => Unauthorized(new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Unauthorized",
                    Detail = error.Message
                }),
                ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Forbidden",
                    Detail = error.Message
                }),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Server Error",
                    Detail = error.Message
                })
            }
        );
    }
}
