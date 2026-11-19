using Asp.Versioning;
using ECommerce.API.Models;
using ECommerce.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ApiControllerBase : ControllerBase
{
    protected ActionResult<ApiResponse<T>> FromResult<T>(Result<T> result)
    {
        return result.Match<ActionResult<ApiResponse<T>>>(
            value => Ok(ApiResponse<T>.SuccessResponse(value)),
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
