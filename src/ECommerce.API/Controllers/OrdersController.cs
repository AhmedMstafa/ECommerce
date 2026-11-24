using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Asp.Versioning;
using ECommerce.API.Contracts.Orders;
using ECommerce.API.Models;
using ECommerce.Domain.Common;
using ECommerce.UseCases.Orders.Commands.CancelOrder;
using ECommerce.UseCases.Orders.Commands.CreateOrder;
using ECommerce.UseCases.Orders.Commands.RetryPayment;
using ECommerce.UseCases.Orders.Dtos;
using ECommerce.UseCases.Orders.Queries.GetMyOrderById;
using ECommerce.UseCases.Orders.Queries.GetMyOrders;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ECommerce.API.Controllers;

[ApiVersion("1.0")]
[Authorize]
[EnableRateLimiting("fixed")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public class OrdersController(ISender sender) : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken ct)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null) return UnauthorizedProblem();

        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Error",
                Detail = "The Idempotency-Key header is required."
            });
        }

        if (idempotencyKey.Length > 128)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Error",
                Detail = "The Idempotency-Key header must not exceed 128 characters."
            });
        }

        var result = await sender.Send(
            new CreateOrderCommand(userId.Value, request.AddressId, request.DeliveryMethodId, idempotencyKey),
            ct);

        return FromResult(result);
    }

    [HttpGet("my")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrderResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderResponse>>>> GetMyOrders(CancellationToken ct)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null) return UnauthorizedProblem();

        var result = await sender.Send(new GetMyOrdersQuery(userId.Value), ct);
        return FromResult(result);
    }

    [HttpGet("my/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> GetMyOrderById(Guid id, CancellationToken ct)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null) return UnauthorizedProblem();

        var result = await sender.Send(new GetMyOrderByIdQuery(userId.Value, id), ct);
        return FromResult(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> Cancel(Guid id, CancellationToken ct)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null) return UnauthorizedProblem();

        var result = await sender.Send(new CancelOrderCommand(userId.Value, id, IsAdmin: false), ct);
        return FromResult(result);
    }

    [HttpPost("{id:guid}/retry-payment")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> RetryPayment(Guid id, CancellationToken ct)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null) return UnauthorizedProblem();

        var result = await sender.Send(new RetryPaymentCommand(userId.Value, id), ct);
        return FromResult(result);
    }

    private Guid? GetAuthenticatedUserId()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(userIdValue, out var userId) ? userId : null;
    }

    private ActionResult UnauthorizedProblem() =>
        Unauthorized(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Detail = "A valid authenticated user is required."
        });
}