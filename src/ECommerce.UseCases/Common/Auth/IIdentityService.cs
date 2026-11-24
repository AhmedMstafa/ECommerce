using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Models;
using ECommerce.UseCases.Users.Dtos;

namespace ECommerce.UseCases.Common.Auth;

public interface IIdentityService
{
    Task<Result<AuthResponse>> RegisterAsync(
        string email,
        string password,
        string firstName,
        string lastName,
        CancellationToken ct = default);

    Task<Result<AuthResponse>> LoginAsync(
        string email,
        string password,
        CancellationToken ct = default);

    Task<Result<AuthResponse>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken ct = default);

    Task<Result> LogoutAsync(
        Guid userId,
        string refreshToken,
        CancellationToken ct = default);

    Task<Result> ConfirmEmailAsync(
        Guid userId,
        string token,
        CancellationToken ct = default);

    Task<Result> ForgotPasswordAsync(
        string email,
        CancellationToken ct = default);

    Task<Result> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken ct = default);

    Task<Result<UserProfileResponse>> GetCurrentUserAsync(
        Guid userId,
        CancellationToken ct = default);

    Task<Result<UserProfileResponse>> UpdateProfileAsync(
        Guid userId,
        string firstName,
        string lastName,
        Optional<string?> description,
        string? pictureUrl,
        CancellationToken ct = default);

    Task<Result<AddressResponse>> AddAddressAsync(
        Guid userId,
        string firstName,
        string lastName,
        string street,
        string city,
        string state,
        string zipCode,
        string country,
        bool isDefault,
        CancellationToken ct = default);

    Task<Result> RemoveAddressAsync(
        Guid userId,
        Guid addressId,
        CancellationToken ct = default);

    Task<Result<AddressResponse>> GetDefaultAddressAsync(
        Guid userId,
        CancellationToken ct = default);

    Task<Result<AuthResponse>> GoogleLoginAsync(
        string idToken,
        CancellationToken ct = default);

    Task<Result> LinkGoogleAccountAsync(
        string idToken,
        Guid authenticatedUserId,
        CancellationToken ct = default);
}
