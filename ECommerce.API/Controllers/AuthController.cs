using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Asp.Versioning;
using ECommerce.API.Models;
using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.UseCases.Auth.Commands.ForgotPassword;
using ECommerce.UseCases.Auth.Commands.GoogleLogin;
using ECommerce.UseCases.Auth.Commands.LinkGoogleAccount;
using ECommerce.UseCases.Auth.Commands.Login;
using ECommerce.UseCases.Auth.Commands.Register;
using ECommerce.UseCases.Auth.Commands.ResetPassword;
using ECommerce.UseCases.Baskets.Commands.MergeBasket;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Common.Baskets;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ECommerce.API.Controllers;

/// <summary>
/// Provides authentication and account management endpoints.
/// </summary>
[ApiVersion("1.0")]
[EnableRateLimiting("auth-policy")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public class AuthController(ISender sender) : ApiControllerBase
{
    private const string GuestCookieName = "guest_basket_token";

    /// <summary>
    /// Registers a new user account.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register(
        [FromBody] RegisterCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(command, cancellationToken);
        if (result.IsSuccess)
        {
            await TryMergeGuestBasketAsync(result.Value.Token, cancellationToken);
        }

        return FromResult(result);
    }

    /// <summary>
    /// Authenticates a user with email and password.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login(
        [FromBody] LoginCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(command, cancellationToken);
        if (result.IsSuccess)
        {
            await TryMergeGuestBasketAsync(result.Value.Token, cancellationToken);
        }

        return FromResult(result);
    }

    /// <summary>
    /// Initiates a secure password reset workflow via email and transactional outbox.
    /// </summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<string>>> ForgotPassword(
        [FromBody] ForgotPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return FromResult(Result.Failure<string>(result.Error!));
        }

        return Ok(ApiResponse<string>.SuccessResponse("If your email is registered, you will receive password reset instructions."));
    }

    /// <summary>
    /// Completes the password reset process using a one-time token.
    /// </summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<string>>> ResetPassword(
        [FromBody] ResetPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return FromResult(Result.Failure<string>(result.Error!));
        }

        return Ok(ApiResponse<string>.SuccessResponse("Password has been reset successfully. You can now log in."));
    }

    /// <summary>
    /// Authenticates or registers a user using Google Sign-In identity token.
    /// </summary>
    [HttpPost("google-login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> GoogleLogin(
        [FromBody] GoogleLoginCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(command, cancellationToken);
        if (result.IsSuccess)
        {
            await TryMergeGuestBasketAsync(result.Value.Token, cancellationToken);
        }

        return FromResult(result);
    }

    /// <summary>
    /// Links a verified Google identity to the currently authenticated account.
    /// The user id is read from the token principal, never from the request body.
    /// </summary>
    [HttpPost("link-google")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<string>>> LinkGoogle(
        [FromBody] LinkGoogleAccountRequest request,
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

        var result = await sender.Send(
            new LinkGoogleAccountCommand(request.IdToken, userId),
            cancellationToken);

        if (result.IsFailure)
        {
            return FromResult(Result.Failure<string>(result.Error!));
        }

        return Ok(ApiResponse<string>.SuccessResponse("Google account linked successfully."));
    }

    private async Task TryMergeGuestBasketAsync(string token, CancellationToken cancellationToken)
    {
        var guestToken = ReadGuestToken();
        if (string.IsNullOrWhiteSpace(guestToken) || !GuestToken.IsValid(guestToken))
        {
            return;
        }

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var userIdValue = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                          ?? jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return;
        }

        try
        {
            var merge = await sender.Send(
                new MergeBasketCommand(BasketKey.ForUser(userId), guestToken),
                cancellationToken);

            // The guest cookie is cleared only after a successful merge so the
            // explicit /basket/merge endpoint can retry if storage was unavailable.
            if (merge.IsSuccess)
            {
                DeleteGuestCookie();
            }
        }
        catch
        {
            // Authentication must not fail because the temporary basket store is
            // unavailable. The guest token remains valid for an explicit retry.
        }
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
}

/// <summary>
/// Request body for explicit Google account linking.
/// </summary>
public sealed record LinkGoogleAccountRequest(string IdToken);
