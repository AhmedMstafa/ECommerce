using System.Security.Cryptography;
using System.Text;
using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Auth;
using ECommerce.Infrastructure.Persistence;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Common.Models;
using ECommerce.UseCases.Users.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerce.Infrastructure.Identity;

public class IdentityService(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IJwtProvider jwtProvider,
    IGoogleTokenValidator googleTokenValidator,
    StoreDbContext dbContext,
    IConfiguration configuration,
    IOptions<JwtSettings> jwtOptions,
    ILogger<IdentityService> logger) : IIdentityService
{
    private const string GoogleProvider = "Google";

    public async Task<Result<AuthResponse>> RegisterAsync(
        string email,
        string password,
        string firstName,
        string lastName,
        CancellationToken ct = default)
    {
        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            return Result.Failure<AuthResponse>(
                Error.Conflict("User.EmailAlreadyExists", "A user with this email already exists."));
        }

        var clientBaseUrl = configuration["ClientApp:BaseUrl"];
        if (string.IsNullOrWhiteSpace(clientBaseUrl))
        {
            logger.LogError("ClientApp:BaseUrl is not configured; confirmation email cannot be queued");
            return Result.Failure<AuthResponse>(Error.Failure(
                "EmailConfirmation.ClientNotConfigured",
                "Email confirmation is temporarily unavailable."));
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            EmailConfirmed = false
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            var firstError = createResult.Errors.FirstOrDefault()?.Description ?? "Registration failed";
            return Result.Failure<AuthResponse>(Error.Validation("User.RegistrationFailed", firstError));
        }

        var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var outboxResult = EmailConfirmationOutboxFactory.Create(
            clientBaseUrl,
            user.Email!,
            user.FirstName ?? string.Empty,
            user.Id,
            confirmationToken);
        if (outboxResult.IsFailure)
        {
            // Compensate: user was created but confirmation cannot be queued.
            await userManager.DeleteAsync(user);
            return Result.Failure<AuthResponse>(outboxResult.Error!);
        }

        dbContext.OutboxMessages.Add(outboxResult.Value);
        return await CreateAuthResponseAsync(user, ct);
    }

    public async Task<Result<AuthResponse>> LoginAsync(
        string email,
        string password,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return Result.Failure<AuthResponse>(
                Error.Validation("User.InvalidCredentials", "Invalid email or password."));
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return Result.Failure<AuthResponse>(
                Error.Conflict("User.LockedOut", "This account has been temporarily locked due to too many failed attempts."));
        }

        if (!result.Succeeded)
        {
            return Result.Failure<AuthResponse>(
                Error.Validation("User.InvalidCredentials", "Invalid email or password."));
        }

        return await CreateAuthResponseAsync(user, ct);
    }

    public async Task<Result> ForgotPasswordAsync(string email, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(email);

        // Always report success for unknown addresses to prevent account enumeration.
        if (user is null)
        {
            return Result.Success();
        }

        var clientBaseUrl = configuration["ClientApp:BaseUrl"];
        if (string.IsNullOrWhiteSpace(clientBaseUrl))
        {
            logger.LogError("ClientApp:BaseUrl is not configured; password reset email was not queued");
            return Result.Failure(Error.Failure(
                "PasswordReset.ClientNotConfigured",
                "Password reset is temporarily unavailable."));
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        // Application rules own link construction, HTML encoding, the outbox
        // payload, and its idempotency key. Infrastructure only persists it.
        var outboxResult = PasswordResetOutboxFactory.Create(
            clientBaseUrl,
            user.Email!,
            user.FirstName ?? string.Empty,
            user.Id,
            token);
        if (outboxResult.IsFailure)
        {
            return Result.Failure(outboxResult.Error!);
        }

        dbContext.OutboxMessages.Add(outboxResult.Value);
        await dbContext.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return Result.Failure(Error.Validation("PasswordReset.Invalid", "Invalid password reset request."));
        }

        var decodedToken = Uri.UnescapeDataString(token);
        var resetResult = await userManager.ResetPasswordAsync(user, decodedToken, newPassword);

        if (!resetResult.Succeeded)
        {
            var firstError = resetResult.Errors.FirstOrDefault()?.Description ?? "Failed to reset password.";
            return Result.Failure(Error.Validation("PasswordReset.Failed", firstError));
        }

        var utcNow = DateTime.UtcNow;
        await RevokeAllActiveUserRefreshTokensAsync(user.Id, utcNow, "Password reset", ct);
        await dbContext.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<AuthResponse>> GoogleLoginAsync(
        string idToken,
        CancellationToken ct = default)
    {
        var validation = await googleTokenValidator.ValidateAsync(idToken, ct);
        if (validation.IsFailure)
        {
            return Result.Failure<AuthResponse>(validation.Error!);
        }

        var identity = validation.Value!;

        // Only a previously linked provider key may authenticate an existing account.
        // Matching email addresses are never enough to link a Google identity.
        var linkedUser = await userManager.FindByLoginAsync(GoogleProvider, identity.Subject);
        var emailOwner = linkedUser is null
            ? await userManager.FindByEmailAsync(identity.Email)
            : null;

        var decision = GoogleLinkingRules.DecideLogin(linkedUser is not null, emailOwner is not null);

        if (decision == GoogleLoginDecision.RejectUnlinkedEmailOwner)
        {
            return Result.Failure<AuthResponse>(Error.Conflict(
                "GoogleAuth.EmailAlreadyRegistered",
                "An account with this email already exists. Sign in with your password and link Google from your account settings."));
        }

        if (decision == GoogleLoginDecision.UseLinkedUser)
        {
            return await CreateAuthResponseAsync(linkedUser!, ct);
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = identity.Email,
            Email = identity.Email,
            FirstName = identity.FirstName ?? "GoogleUser",
            LastName = identity.LastName ?? string.Empty,
            PictureUrl = identity.Picture,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            return Result.Failure<AuthResponse>(Error.Validation(
                "User.CreationFailed",
                createResult.Errors.FirstOrDefault()?.Description ?? "Failed to create user."));
        }

        var addLoginResult = await userManager.AddLoginAsync(
            user, new UserLoginInfo(GoogleProvider, identity.Subject, GoogleProvider));

        if (!addLoginResult.Succeeded)
        {
            return Result.Failure<AuthResponse>(Error.Validation(
                "GoogleAuth.LinkFailed",
                addLoginResult.Errors.FirstOrDefault()?.Description ?? "Failed to link Google account."));
        }

        return await CreateAuthResponseAsync(user, ct);
    }

    public async Task<Result> LinkGoogleAccountAsync(
        string idToken,
        Guid authenticatedUserId,
        CancellationToken ct = default)
    {
        var validation = await googleTokenValidator.ValidateAsync(idToken, ct);
        if (validation.IsFailure)
        {
            return Result.Failure(validation.Error!);
        }

        var identity = validation.Value!;
        var user = await userManager.FindByIdAsync(authenticatedUserId.ToString());

        if (user is null)
        {
            return Result.Failure(Error.Unauthorized("Auth.Unauthenticated", "You must be signed in to link an account."));
        }

        if (!GoogleLinkingRules.CanExplicitlyLink(user.Email, identity.Email))
        {
            return Result.Failure(Error.Forbidden(
                "GoogleAuth.EmailMismatch",
                "The Google account email must match the email of the signed-in user."));
        }

        var owner = await userManager.FindByLoginAsync(GoogleProvider, identity.Subject);
        if (owner is not null)
        {
            return owner.Id == user.Id
                ? Result.Success()
                : Result.Failure(Error.Conflict(
                    "GoogleAuth.AlreadyLinked",
                    "This Google account is already linked to a different user."));
        }

        var addResult = await userManager.AddLoginAsync(
            user, new UserLoginInfo(GoogleProvider, identity.Subject, GoogleProvider));

        return addResult.Succeeded
            ? Result.Success()
            : Result.Failure(Error.Validation(
                "GoogleAuth.LinkFailed",
                addResult.Errors.FirstOrDefault()?.Description ?? "Failed to link Google account."));
    }

    public async Task<Result<AuthResponse>> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result.Failure<AuthResponse>(Error.Validation("Auth.InvalidRefreshToken", "Refresh token is required."));
        }

        var tokenHash = HashRefreshToken(refreshToken);
        var utcNow = DateTime.UtcNow;

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
            try
            {
                var storedToken = await dbContext.RefreshTokens
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.TokenHash == tokenHash && !x.IsDeleted, ct);

                if (storedToken is null)
                {
                    return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.InvalidRefreshToken", "Invalid refresh token."));
                }

                if (storedToken.RevokedAtUtc is not null || storedToken.ReplacedByTokenHash is not null)
                {
                    // Reuse of a rotated/revoked token - revoke active tokens for safety.
                    await RevokeAllActiveUserRefreshTokensAsync(storedToken.UserId, utcNow, "Attempted reuse of revoked refresh token", ct);
                    await dbContext.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    logger.LogWarning("Refresh token reuse detected for user {UserId}", storedToken.UserId);
                    return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.RefreshTokenReuse", "Refresh token is invalid or has been reused."));
                }

                if (storedToken.IsExpired(utcNow))
                {
                    storedToken.Revoke(utcNow, "Expired");
                    await dbContext.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.RefreshTokenExpired", "Refresh token has expired."));
                }

                var user = await userManager.FindByIdAsync(storedToken.UserId.ToString());
                if (user is null)
                {
                    return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.InvalidRefreshToken", "Invalid refresh token."));
                }

                // Atomic rotation: only revoke if still active/unrevoked.
                var revokedRows = await dbContext.RefreshTokens
                    .IgnoreQueryFilters()
                    .Where(x => x.Id == storedToken.Id && x.RevokedAtUtc == null && !x.IsDeleted)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.RevokedAtUtc, utcNow)
                        .SetProperty(x => x.ReasonRevoked, "Rotated")
                        .SetProperty(x => x.UpdatedAt, new DateTimeOffset(utcNow, TimeSpan.Zero)), ct);

                if (revokedRows == 0)
                {
                    await transaction.RollbackAsync(ct);
                    return Result.Failure<AuthResponse>(Error.Unauthorized("Auth.RefreshTokenReuse", "Refresh token is invalid or has been reused."));
                }

                var (plainToken, refreshEntity) = CreateRefreshTokenEntity(user.Id, utcNow);
                // set replaced-by on old token
                await dbContext.RefreshTokens
                    .IgnoreQueryFilters()
                    .Where(x => x.Id == storedToken.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.ReplacedByTokenHash, refreshEntity.TokenHash), ct);

                dbContext.RefreshTokens.Add(refreshEntity);
                await dbContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                var auth = await BuildAuthResponseAsync(user, plainToken, refreshEntity.ExpiresAtUtc, ct);
                return Result.Success(auth);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<Result> LogoutAsync(Guid userId, string refreshToken, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure(Error.Unauthorized("Auth.Unauthenticated", "You must be signed in to log out."));
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result.Failure(Error.Validation("Auth.InvalidRefreshToken", "Refresh token is required."));
        }

        var tokenHash = HashRefreshToken(refreshToken);
        var utcNow = DateTime.UtcNow;
        var storedToken = await dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash && !x.IsDeleted, ct);

        if (storedToken is null)
        {
            return Result.Failure(Error.Unauthorized("Auth.InvalidRefreshToken", "Invalid refresh token."));
        }

        if (storedToken.UserId != userId)
        {
            return Result.Failure(Error.Forbidden("Auth.RefreshTokenOwnership", "You cannot revoke another user's refresh token."));
        }

        if (storedToken.RevokedAtUtc is null)
        {
            storedToken.Revoke(utcNow, "Logout");
            await dbContext.SaveChangesAsync(ct);
        }

        return Result.Success();
    }

    public async Task<Result> ConfirmEmailAsync(Guid userId, string token, CancellationToken ct = default)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure(Error.Validation("EmailConfirmation.Invalid", "Invalid email confirmation request."));
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.IsDeleted)
        {
            return Result.Failure(Error.Validation("EmailConfirmation.Invalid", "Invalid email confirmation request."));
        }

        if (user.EmailConfirmed)
        {
            return Result.Success();
        }

        var decodedToken = Uri.UnescapeDataString(token);
        var result = await userManager.ConfirmEmailAsync(user, decodedToken);
        if (!result.Succeeded)
        {
            var firstError = result.Errors.FirstOrDefault()?.Description ?? "Email confirmation failed.";
            logger.LogWarning("Email confirmation failed for user {UserId}: {Error}", userId, firstError);
            return Result.Failure(Error.Validation("EmailConfirmation.Failed", "The email confirmation token is invalid, expired, or already used."));
        }

        return Result.Success();
    }

    public async Task<Result<UserProfileResponse>> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<UserProfileResponse>(Error.Unauthorized("Auth.Unauthenticated", "You must be signed in."));
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.IsDeleted)
        {
            return Result.Failure<UserProfileResponse>(Error.NotFound("User.NotFound", "User was not found."));
        }

        var roles = await userManager.GetRolesAsync(user);
        return Result.Success(MapUserProfile(user, roles));
    }

    public async Task<Result<UserProfileResponse>> UpdateProfileAsync(
        Guid userId,
        string firstName,
        string lastName,
        Optional<string?> description,
        string? pictureUrl,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<UserProfileResponse>(Error.Unauthorized("Auth.Unauthenticated", "You must be signed in."));
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.IsDeleted)
        {
            return Result.Failure<UserProfileResponse>(Error.NotFound("User.NotFound", "User was not found."));
        }

        user.FirstName = firstName.Trim();
        user.LastName = lastName.Trim();
        if (pictureUrl is not null)
        {
            user.PictureUrl = string.IsNullOrWhiteSpace(pictureUrl) ? null : pictureUrl.Trim();
        }

        if (description.HasValue)
        {
            user.Description = string.IsNullOrWhiteSpace(description.Value) ? null : description.Value.Trim();
        }

        user.UpdatedAt = DateTimeOffset.UtcNow;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var firstError = updateResult.Errors.FirstOrDefault()?.Description ?? "Profile update failed.";
            return Result.Failure<UserProfileResponse>(Error.Validation("User.UpdateFailed", firstError));
        }

        var roles = await userManager.GetRolesAsync(user);
        return Result.Success(MapUserProfile(user, roles));
    }

    public async Task<Result<AddressResponse>> AddAddressAsync(
        Guid userId,
        string firstName,
        string lastName,
        string street,
        string city,
        string state,
        string zipCode,
        string country,
        bool isDefault,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<AddressResponse>(Error.Unauthorized("Auth.Unauthenticated", "You must be signed in."));
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.IsDeleted)
        {
            return Result.Failure<AddressResponse>(Error.NotFound("User.NotFound", "User was not found."));
        }

        var utcNow = DateTime.UtcNow;
        var addressResult = Address.Create(userId, firstName, lastName, street, city, state, zipCode, country, isDefault, utcNow);
        if (addressResult.IsFailure)
        {
            return Result.Failure<AddressResponse>(addressResult.Error!);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
            try
            {
                var hasAddresses = await dbContext.Addresses.AnyAsync(x => x.UserId == userId, ct);
                var address = addressResult.Value;

                if (!hasAddresses)
                {
                    address.SetDefault(true, utcNow);
                }
                else if (address.IsDefault)
                {
                    await ClearDefaultAddressesAsync(userId, utcNow, ct);
                }

                dbContext.Addresses.Add(address);
                await dbContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Result.Success(MapAddress(address));
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<Result> RemoveAddressAsync(Guid userId, Guid addressId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure(Error.Unauthorized("Auth.Unauthenticated", "You must be signed in."));
        }

        if (addressId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Address.InvalidId", "A valid address id is required."));
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
            try
            {
                var address = await dbContext.Addresses.FirstOrDefaultAsync(x => x.Id == addressId, ct);
                if (address is null)
                {
                    return Result.Failure(Error.NotFound("Address.NotFound", "Address was not found."));
                }

                if (address.UserId != userId)
                {
                    return Result.Failure(Error.Forbidden("Address.Ownership", "You cannot remove another user's address."));
                }

                var wasDefault = address.IsDefault;
                var deleteResult = address.MarkAsDeleted();
                if (deleteResult.IsFailure)
                {
                    return deleteResult;
                }

                address.SetDefault(false, DateTime.UtcNow);
                await dbContext.SaveChangesAsync(ct);

                if (wasDefault)
                {
                    var nextDefault = await dbContext.Addresses
                        .Where(x => x.UserId == userId)
                        .OrderByDescending(x => x.CreatedAt)
                        .FirstOrDefaultAsync(ct);

                    if (nextDefault is not null)
                    {
                        nextDefault.SetDefault(true, DateTime.UtcNow);
                        await dbContext.SaveChangesAsync(ct);
                    }
                }

                await transaction.CommitAsync(ct);
                return Result.Success();
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<Result<AddressResponse>> GetDefaultAddressAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<AddressResponse>(Error.Unauthorized("Auth.Unauthenticated", "You must be signed in."));
        }

        var address = await dbContext.Addresses
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsDefault)
            .FirstOrDefaultAsync(ct);

        if (address is null)
        {
            return Result.Failure<AddressResponse>(Error.NotFound("Address.DefaultNotFound", "A default address was not found."));
        }

        return Result.Success(MapAddress(address));
    }

    private async Task<AuthResponse> CreateAuthResponseAsync(AppUser user, CancellationToken ct = default)
    {
        var utcNow = DateTime.UtcNow;
        var (plainToken, refreshEntity) = CreateRefreshTokenEntity(user.Id, utcNow);
        dbContext.RefreshTokens.Add(refreshEntity);
        await dbContext.SaveChangesAsync(ct);
        return await BuildAuthResponseAsync(user, plainToken, refreshEntity.ExpiresAtUtc, ct);
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(AppUser user, string plainRefreshToken, DateTime refreshTokenExpiresAtUtc, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);
        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        var accessExpiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.Value.ExpirationMinutes);
        var accessToken = jwtProvider.GenerateToken(user.Id, user.Email!, fullName, roles);
        return new AuthResponse(accessToken, user.Email!, fullName, accessExpiresAt, plainRefreshToken, refreshTokenExpiresAtUtc);
    }

    private (string PlainToken, RefreshToken Entity) CreateRefreshTokenEntity(Guid userId, DateTime utcNow)
    {
        var plainToken = GenerateRefreshToken();
        var hash = HashRefreshToken(plainToken);
        var expiresAt = utcNow.AddDays(Math.Max(1, jwtOptions.Value.RefreshTokenExpirationDays));
        var createResult = RefreshToken.Create(userId, hash, utcNow, expiresAt);
        if (createResult.IsFailure)
        {
            throw new InvalidOperationException(createResult.Error!.Message);
        }

        return (plainToken, createResult.Value);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string HashRefreshToken(string refreshToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes);
    }

    private async Task RevokeAllActiveUserRefreshTokensAsync(Guid userId, DateTime utcNow, string reason, CancellationToken ct)
    {
        var activeTokens = await dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null && !x.IsDeleted && x.ExpiresAtUtc > utcNow)
            .ToListAsync(ct);

        foreach (var token in activeTokens)
        {
            token.Revoke(utcNow, reason);
        }
    }

    private async Task ClearDefaultAddressesAsync(Guid userId, DateTime utcNow, CancellationToken ct, Guid? exceptAddressId = null)
    {
        var defaults = await dbContext.Addresses
            .Where(x => x.UserId == userId && x.IsDefault && (!exceptAddressId.HasValue || x.Id != exceptAddressId.Value))
            .ToListAsync(ct);

        foreach (var address in defaults)
        {
            address.SetDefault(false, utcNow);
        }
    }

    private static UserProfileResponse MapUserProfile(AppUser user, IEnumerable<string> roles) =>
        new(user.Id, user.Email ?? string.Empty, user.FirstName, user.LastName, user.Description, user.PictureUrl, roles.ToArray());

    private static AddressResponse MapAddress(Address address) =>
        new(address.Id, address.FirstName, address.LastName, address.Street, address.City, address.State, address.ZipCode, address.Country, address.IsDefault);
}
