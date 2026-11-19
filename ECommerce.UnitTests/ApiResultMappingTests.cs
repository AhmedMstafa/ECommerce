using ECommerce.API.Controllers;
using ECommerce.API.Models;
using ECommerce.Domain.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.UnitTests;

public class ApiResultMappingTests
{
    private readonly TestController _controller = new();

    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.Failure, StatusCodes.Status500InternalServerError)]
    public void FromResult_ShouldMapErrorTypeToStatusCode(ErrorType errorType, int expectedStatusCode)
    {
        var error = new Error("Test.Code", "Test message", errorType);

        var actionResult = _controller.Invoke<string>(Result.Failure<string>(error));
        var objectResult = actionResult.Result as ObjectResult;

        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(expectedStatusCode);
        objectResult.Value.Should().BeOfType<ProblemDetails>();
    }

    [Fact]
    public void FromResult_ShouldWrapSuccessfulResult()
    {
        var actionResult = _controller.Invoke<string>(Result<string>.Success("done"));
        var ok = actionResult.Result as OkObjectResult;

        ok.Should().NotBeNull();
        ok!.Value.Should().BeOfType<ApiResponse<string>>();
    }

    private sealed class TestController : ApiControllerBase
    {
        public ActionResult<ApiResponse<T>> Invoke<T>(Result<T> result) => FromResult(result);
    }
}
