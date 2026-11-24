using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Asp.Versioning;
using ECommerce.API.Contracts.Basket;
using ECommerce.API.Models;
using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.UseCases.Baskets.Commands.AddBasketItem;
using ECommerce.UseCases.Baskets.Commands.ClearBasket;
using ECommerce.UseCases.Baskets.Commands.MergeBasket;
using ECommerce.UseCases.Baskets.Commands.RemoveBasketItem;
using ECommerce.UseCases.Baskets.Commands.UpdateBasketItemQuantity;
using ECommerce.UseCases.Baskets.Dtos;
using ECommerce.UseCases.Baskets.Queries.GetBasket;
using ECommerce.UseCases.Common.Baskets;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ECommerce.API.Controllers;

/// <summary>
/// Temporary shopping basket stored in Redis. Supports anonymous guests and
/// authenticated users. Responses always reflect current product data from SQL.
/// </summary>
[ApiVersion("1.0")]
[EnableRateLimiting("basket-policy")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public class BasketController(ISender sender) : ApiControllerBase
{
    private const string GuestCookieName = "guest_basket_token";

    /// <summary>
    /// Returns the current basket with current prices and availability from SQL.
    /// Anonymous guests may send <c>X-Guest-Token</c> or the <c>guest_basket_token</c>
    /// cookie; when neither is present a new guest token is issued.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<BasketResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<BasketResponse>>> Get(
        CancellationToken cancellationToken = default)
    {
        var context = ResolveBasketContext(allowCreateGuestToken: true);
        var result = await sender.Send(new GetBasketQuery(context.Key), cancellationToken);
        return ApplyGuestToken(FromResult(result), context);
    }

    /// <summary>
    /// Adds a product to the basket or increases its quantity when it already
    /// exists. Validates current availability in SQL; no inventory is reserved.
    /// </summary>
    [HttpPost("items")]
    [ProducesResponseType(typeof(ApiResponse<BasketResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<BasketResponse>>> AddItem(
        [FromBody] AddBasketItemRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = ResolveBasketContext(allowCreateGuestToken: true);
        var result = await sender.Send(
            new AddBasketItemCommand(context.Key, request.ProductId, request.Quantity),
            cancellationToken);
        return ApplyGuestToken(FromResult(result), context);
    }

    /// <summary>
    /// Removes a product from the basket. Removing a missing item returns the
    /// current basket without error.
    /// </summary>
    [HttpDelete("items/{productId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<BasketResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<BasketResponse>>> RemoveItem(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var context = ResolveBasketContext(allowCreateGuestToken: true);
        var result = await sender.Send(
            new RemoveBasketItemCommand(context.Key, productId),
            cancellationToken);
        return ApplyGuestToken(FromResult(result), context);
    }

    /// <summary>
    /// Sets the quantity for a basket item after validating current stock.
    /// </summary>
    [HttpPut("items/{productId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<BasketResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<BasketResponse>>> UpdateQuantity(
        Guid productId,
        [FromBody] UpdateBasketItemQuantityRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = ResolveBasketContext(allowCreateGuestToken: true);
        var result = await sender.Send(
            new UpdateBasketItemQuantityCommand(context.Key, productId, request.Quantity),
            cancellationToken);
        return ApplyGuestToken(FromResult(result), context);
    }

    /// <summary>
    /// Removes all items from the current basket.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(typeof(ApiResponse<BasketResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<BasketResponse>>> Clear(
        CancellationToken cancellationToken = default)
    {
        var context = ResolveBasketContext(allowCreateGuestToken: true);
        var result = await sender.Send(new ClearBasketCommand(context.Key), cancellationToken);
        return ApplyGuestToken(FromResult(result), context);
    }

    /// <summary>
    /// Merges the current guest basket into the authenticated user's basket.
    /// Requires a bearer token; the guest token is read from <c>X-Guest-Token</c>
    /// or the <c>guest_basket_token</c> cookie. Idempotent and retry-safe:
    /// a previously merged guest basket is not added again.
    /// </summary>
    [HttpPost("merge")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<BasketResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<BasketResponse>>> Merge(
        CancellationToken cancellationToken = default)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A valid authenticated user is required."
            });
        }

        var guestToken = ReadGuestToken();
        if (string.IsNullOrWhiteSpace(guestToken))
        {
            var current = await sender.Send(new GetBasketQuery(BasketKey.ForUser(userId)), cancellationToken);
            return FromResult(current);
        }

        if (!GuestToken.IsValid(guestToken))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = BasketErrors.GuestTokenRequired.Message
            });
        }

        var result = await sender.Send(
            new MergeBasketCommand(BasketKey.ForUser(userId), guestToken),
            cancellationToken);

        if (result.IsSuccess)
        {
            DeleteGuestCookie();
        }

        return FromResult(result);
    }

    private BasketContext ResolveBasketContext(bool allowCreateGuestToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (Guid.TryParse(userIdValue, out var userId))
        {
            return new BasketContext(BasketKey.ForUser(userId), null, false);
        }

        var token = ReadGuestToken();
        if (GuestToken.IsValid(token))
        {
            return new BasketContext(BasketKey.ForGuest(GuestToken.Hash(token!)), token, false);
        }

        if (!allowCreateGuestToken)
        {
            var generated = GuestToken.Generate();
            return new BasketContext(BasketKey.ForGuest(GuestToken.Hash(generated)), generated, true);
        }

        var newToken = GuestToken.Generate();
        return new BasketContext(BasketKey.ForGuest(GuestToken.Hash(newToken)), newToken, true);
    }

    private string? ReadGuestToken()
    {
        if (Request.Headers.TryGetValue(GuestToken.HeaderName, out var headerValue))
        {
            var headerToken = headerValue.ToString();
            if (!string.IsNullOrWhiteSpace(headerToken))
            {
                return headerToken;
            }
        }

        return Request.Cookies.TryGetValue(GuestCookieName, out var cookieToken)
            ? cookieToken
            : null;
    }

    private ActionResult<ApiResponse<BasketResponse>> ApplyGuestToken(
        ActionResult<ApiResponse<BasketResponse>> response,
        BasketContext context)
    {
        // The cookie/header carries the opaque token only; the Redis key always
        // uses its hash. Never issue a token for an authenticated user basket.
        if (context.Key.Kind != BasketKind.Guest || string.IsNullOrWhiteSpace(context.GuestToken))
        {
            return response;
        }

        if (context.IsNewToken || !Request.Cookies.ContainsKey(GuestCookieName))
        {
            Response.Cookies.Append(GuestCookieName, context.GuestToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });
        }

        Response.Headers[GuestToken.ResponseHeaderName] = context.GuestToken;
        return response;
    }

    private void DeleteGuestCookie()
    {
        if (Request.Cookies.ContainsKey(GuestCookieName))
        {
            Response.Cookies.Delete(GuestCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                IsEssential = true
            });
        }
    }

    private sealed record BasketContext(BasketKey Key, string? GuestToken, bool IsNewToken);
}
