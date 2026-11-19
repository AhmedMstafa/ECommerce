using System.Text.Json;
using System.Web;
using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.BackgroundJobs;
using ECommerce.Infrastructure.Persistence;
using ECommerce.UseCases.Common.Auth;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.Identity;

public class IdentityService(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IJwtProvider jwtProvider,
    StoreDbContext dbContext,
    IConfiguration configuration,
    ILogger<IdentityService> logger) : IIdentityService
{
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

        var roles = await userManager.GetRolesAsync(user);
        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        var token = jwtProvider.GenerateToken(user.Id, user.Email!, fullName, roles);

        return new AuthResponse(
            token, 
            user.Email!, 
            fullName, 
            DateTime.UtcNow.AddMinutes(120));
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

        var roles = await userManager.GetRolesAsync(user);
        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        var token = jwtProvider.GenerateToken(user.Id, user.Email!, fullName, roles);

        return new AuthResponse(
            token, 
            user.Email!, 
            fullName, 
            DateTime.UtcNow.AddMinutes(120));
    }

    public async Task<Result> ForgotPasswordAsync(string email, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        
        // Always return success to prevent email enumeration attacks
        if (user is null)
        {
            return Result.Success();
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = HttpUtility.UrlEncode(token);

        var clientBaseUrl = configuration.GetValue<string>("ClientApp:BaseUrl") ?? "https://localhost:5001";
        var resetLink = $"{clientBaseUrl}/reset-password?email={HttpUtility.UrlEncode(user.Email)}&token={encodedToken}";

        var emailPayload = new EmailOutboxPayload
        {
            To = user.Email!,
            Subject = "Password Reset Request",
            Body = $@"
                <h2>Password Reset Request</h2>
                <p>Hello {user.FirstName},</p>
                <p>We received a request to reset your password. Click the link below to set a new password:</p>
                <p><a href='{resetLink}' style='padding: 10px 18px; background-color: #007bff; color: white; text-decoration: none; border-radius: 4px;'>Reset Password</a></p>
                <p>If you did not request this, you can safely ignore this email.</p>
                <p>This link is time-limited and can only be used once.</p>",
            IsHtml = true
        };

        // Write to Transactional Outbox table in the same database context
        var outboxMessageResult = OutboxMessage.Create("Email", JsonSerializer.Serialize(emailPayload));
        if (outboxMessageResult.IsSuccess)
        {
            dbContext.OutboxMessages.Add(outboxMessageResult.Value);
            await dbContext.SaveChangesAsync(ct);
        }

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

        var decodedToken = HttpUtility.UrlDecode(token);
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
        try
        {
            // Validate Google ID Token securely
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken);

            if (payload is null)
            {
                return Result.Failure<AuthResponse>(
                    Error.Validation("GoogleAuth.InvalidToken", "Invalid Google ID token."));
            }

            // Look up existing user by Google Login Provider or Email
            var user = await userManager.FindByLoginAsync("Google", payload.Subject)
                       ?? await userManager.FindByEmailAsync(payload.Email);

            if (user is null)
            {
                // Register new user authenticated via Google
                user = new AppUser
                {
                    Id = Guid.NewGuid(),
                    UserName = payload.Email,
                    Email = payload.Email,
                    FirstName = payload.GivenName ?? "GoogleUser",
                    LastName = payload.FamilyName ?? string.Empty,
                    PictureUrl = payload.Picture,
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    var firstError = createResult.Errors.FirstOrDefault()?.Description ?? "Failed to create user.";
                    return Result.Failure<AuthResponse>(Error.Validation("User.CreationFailed", firstError));
                }

                await userManager.AddLoginAsync(user, new UserLoginInfo("Google", payload.Subject, "Google"));
            }
            else
            {
                // Link Google account safely if not already linked
                var logins = await userManager.GetLoginsAsync(user);
                if (!logins.Any(l => l.LoginProvider == "Google" && l.ProviderKey == payload.Subject))
                {
                    await userManager.AddLoginAsync(user, new UserLoginInfo("Google", payload.Subject, "Google"));
                }
            }

            var roles = await userManager.GetRolesAsync(user);
            var fullName = $"{user.FirstName} {user.LastName}".Trim();
            var token = jwtProvider.GenerateToken(user.Id, user.Email!, fullName, roles);

            return new AuthResponse(
                token, 
                user.Email!, 
                fullName, 
                DateTime.UtcNow.AddMinutes(120));
        }
        catch (InvalidJwtException ex)
        {
            logger.LogError(ex, "Google token validation failed");
            return Result.Failure<AuthResponse>(
                Error.Validation("GoogleAuth.InvalidToken", "Invalid or expired Google token."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Google login failed unexpectedly");
            return Result.Failure<AuthResponse>(
                Error.Failure("GoogleAuth.Failed", "An error occurred during Google authentication."));
        }
    }
}
