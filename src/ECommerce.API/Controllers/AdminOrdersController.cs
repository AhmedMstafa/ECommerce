using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Asp.Versioning;
using ECommerce.API.Contracts.Orders;
using ECommerce.API.Models;
using ECommerce.Domain.Enums;
using ECommerce.UseCases.Orders.Commands.InitiateRefund;
using ECommerce.UseCases.Orders.Commands.UpdateOrderFulfillment;
using ECommerce.UseCases.Orders.Dtos;
using ECommerce.UseCases.Orders.Queries.GetOrderByIdForAdmin;
using ECommerce.UseCases.Orders.Queries.GetOrdersForAdmin;
using ECommerce.UseCases.Orders.Queries.GetReconciliationOrders;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ECommerce.API.Controllers;

[ApiVersion("1.0")]
[Authorize(Roles = "Admin")]
[Route("api/v{version:apiVersion}/admin/orders")]
[EnableRateLimiting("fixed")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public class AdminOrdersController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrderResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderResponse>>>> GetOrders(
        [FromQuery] OrderStatus? status,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetOrdersForAdminQuery(status, fromUtc, toUtc), ct);
        return FromResult(result);
    }

    [HttpGet("reconciliation")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrderResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderResponse>>>> GetReconciliation(CancellationToken ct)
    {
        var result = await sender.Send(new GetReconciliationOrdersQuery(), ct);
        return FromResult(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> GetById(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new GetOrderByIdForAdminQuery(id), ct);
        return FromResult(result);
    }

    [HttpPost("{id:guid}/fulfillment")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> UpdateFulfillment(
        Guid id,
        [FromBody] UpdateOrderFulfillmentRequest request,
        CancellationToken ct)
    {
        var result = await sender.Send(new UpdateOrderFulfillmentCommand(id, request.Status), ct);
        return FromResult(result);
    }

    [HttpPost("{id:guid}/refund")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> InitiateRefund(Guid id, CancellationToken ct)
    {
        var adminUserId = GetAuthenticatedUserId() ?? Guid.Empty;
        var result = await sender.Send(new InitiateRefundCommand(id, adminUserId), ct);
        return FromResult(result);
    }

    private Guid? GetAuthenticatedUserId()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(userIdValue, out var userId) ? userId : null;
    }
}