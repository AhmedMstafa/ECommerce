using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Asp.Versioning;
using ECommerce.API.Models;
using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Models;
using ECommerce.UseCases.Users.Commands.AddAddress;
using ECommerce.UseCases.Users.Commands.RemoveAddress;
using ECommerce.UseCases.Users.Commands.UpdateProfile;
using ECommerce.UseCases.Users.Dtos;
using ECommerce.UseCases.Users.Queries.GetCurrentUser;
using ECommerce.UseCases.Users.Queries.GetDefaultAddress;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers;

/// <summary>
/// Authenticated user profile and address management.
/// </summary>
[ApiVersion("1.0")]
[Authorize]
public class UsersController(ISender sender) : ApiControllerBase
{
    /// <summary>
    /// Returns the currently authenticated user's profile.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserProfileResponse>>> Me(CancellationToken cancellationToken = default)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A valid authenticated user is required."
            });
        }

        var result = await sender.Send(new GetCurrentUserQuery(userId.Value), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Updates the authenticated user's profile. Description supports partial-update semantics:
    /// omitted preserves the existing value, explicit null clears it, a value updates it.
    /// </summary>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserProfileResponse>>> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A valid authenticated user is required."
            });
        }

        var command = new UpdateProfileCommand(
            userId.Value,
            request.FirstName,
            request.LastName,
            request.Description,
            request.PictureUrl);

        var result = await sender.Send(command, cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Adds an address for the authenticated user.
    /// </summary>
    [HttpPost("addresses")]
    [ProducesResponseType(typeof(ApiResponse<AddressResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AddressResponse>>> AddAddress(
        [FromBody] AddAddressRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A valid authenticated user is required."
            });
        }

        var result = await sender.Send(new AddAddressCommand(
            userId.Value,
            request.FirstName,
            request.LastName,
            request.Street,
            request.City,
            request.State,
            request.ZipCode,
            request.Country,
            request.IsDefault), cancellationToken);

        return FromResult(result);
    }

    /// <summary>
    /// Removes an address owned by the authenticated user.
    /// </summary>
    [HttpDelete("addresses/{addressId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<string>>> RemoveAddress(
        Guid addressId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A valid authenticated user is required."
            });
        }

        var result = await sender.Send(new RemoveAddressCommand(userId.Value, addressId), cancellationToken);
        if (result.IsFailure)
        {
            return FromResult(Result.Failure<string>(result.Error!));
        }

        return Ok(ApiResponse<string>.SuccessResponse("Address removed successfully."));
    }

    /// <summary>
    /// Returns the authenticated user's default address.
    /// </summary>
    [HttpGet("addresses/default")]
    [ProducesResponseType(typeof(ApiResponse<AddressResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AddressResponse>>> GetDefaultAddress(CancellationToken cancellationToken = default)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A valid authenticated user is required."
            });
        }

        var result = await sender.Send(new GetDefaultAddressQuery(userId.Value), cancellationToken);
        return FromResult(result);
    }

    private Guid? GetAuthenticatedUserId()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(userIdValue, out var userId) ? userId : null;
    }
}

public sealed record UpdateProfileRequest(
    string FirstName,
    string LastName,
    Optional<string?> Description,
    string? PictureUrl);

public sealed record AddAddressRequest(
    string FirstName,
    string LastName,
    string Street,
    string City,
    string State,
    string ZipCode,
    string Country,
    bool IsDefault = false);
