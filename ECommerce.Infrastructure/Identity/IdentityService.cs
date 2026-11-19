using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Auth;
using ECommerce.Infrastructure.Persistence;
using ECommerce.UseCases.Common.Auth;
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

        return await CreateAuthResponseAsync(user);
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

        return await CreateAuthResponseAsync(user);
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
            return await CreateAuthResponseAsync(linkedUser!);
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

        return await CreateAuthResponseAsync(user);
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

    private async Task<AuthResponse> CreateAuthResponseAsync(AppUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var fullName = $"{user.FirstName} {user.LastName}".Trim();

        return new AuthResponse(
            jwtProvider.GenerateToken(user.Id, user.Email!, fullName, roles),
            user.Email!,
            fullName,
            DateTime.UtcNow.AddMinutes(jwtOptions.Value.ExpirationMinutes));
    }
}
