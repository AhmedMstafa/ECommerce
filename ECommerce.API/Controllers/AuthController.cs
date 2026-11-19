using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Asp.Versioning;
using ECommerce.API.Models;
using ECommerce.Domain.Common;
using ECommerce.UseCases.Auth.Commands.ForgotPassword;
using ECommerce.UseCases.Auth.Commands.GoogleLogin;
using ECommerce.UseCases.Auth.Commands.LinkGoogleAccount;
using ECommerce.UseCases.Auth.Commands.Login;
using ECommerce.UseCases.Auth.Commands.Register;
using ECommerce.UseCases.Auth.Commands.ResetPassword;
using ECommerce.UseCases.Common.Auth;
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
public class AuthController(ISender sender) : ApiControllerBase
{
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
}

/// <summary>
/// Request body for explicit Google account linking.
/// </summary>
public sealed record LinkGoogleAccountRequest(string IdToken);
