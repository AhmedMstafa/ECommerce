using Asp.Versioning;
using ECommerce.API.Contracts.DeliveryMethods;
using ECommerce.API.Models;
using ECommerce.UseCases.DeliveryMethods.Commands.CreateDeliveryMethod;
using ECommerce.UseCases.DeliveryMethods.Commands.DeactivateDeliveryMethod;
using ECommerce.UseCases.DeliveryMethods.Commands.UpdateDeliveryMethod;
using ECommerce.UseCases.DeliveryMethods.Dtos;
using ECommerce.UseCases.DeliveryMethods.Queries.GetActiveDeliveryMethods;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ECommerce.API.Controllers;

[ApiVersion("1.0")]
[EnableRateLimiting("fixed")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public class DeliveryMethodsController(ISender sender) : ApiControllerBase
{
    [HttpGet("active")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DeliveryMethodResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DeliveryMethodResponse>>>> GetActive(CancellationToken ct)
    {
        var result = await sender.Send(new GetActiveDeliveryMethodsQuery(), ct);
        return FromResult(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<DeliveryMethodResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<DeliveryMethodResponse>>> Create(
        [FromBody] CreateDeliveryMethodRequest request,
        CancellationToken ct)
    {
        var result = await sender.Send(new CreateDeliveryMethodCommand(request.Name, request.Description, request.Price), ct);
        return FromResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<DeliveryMethodResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<DeliveryMethodResponse>>> Update(
        Guid id,
        [FromBody] UpdateDeliveryMethodRequest request,
        CancellationToken ct)
    {
        var result = await sender.Send(new UpdateDeliveryMethodCommand(id, request.Name, request.Description, request.Price), ct);
        return FromResult(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<DeliveryMethodResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<DeliveryMethodResponse>>> Deactivate(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new DeactivateDeliveryMethodCommand(id), ct);
        return FromResult(result);
    }
}