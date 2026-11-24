using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Auth;
using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GoogleHttpClientFactory = Google.Apis.Http.IHttpClientFactory;

namespace ECommerce.Infrastructure.Auth;

public sealed class GoogleTokenValidator(
    IOptions<GoogleAuthSettings> options,
    ILogger<GoogleTokenValidator> logger,
    GoogleHttpClientFactory? httpClientFactory = null) : IGoogleTokenValidator
{
    public const string ResilientHttpClientName = "GoogleTokenValidation";

    private readonly GoogleAuthSettings _settings = options.Value;
    private readonly GoogleHttpClientFactory? _httpClientFactory = httpClientFactory;

    public async Task<Result<GoogleIdentity>> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId))
        {
            return Result.Failure<GoogleIdentity>(Error.Failure(
                "GoogleAuth.NotConfigured",
                "Google authentication is not configured on the server."));
        }

        try
        {
            // Binding the audience is mandatory: without it a token issued for another
            // application would be accepted by this API.
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [_settings.ClientId]
            };

            if (_httpClientFactory is not null)
            {
                settings.HttpClientFactory = _httpClientFactory;
            }

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            if (payload is null || string.IsNullOrWhiteSpace(payload.Subject) ||
                string.IsNullOrWhiteSpace(payload.Email))
            {
                return Result.Failure<GoogleIdentity>(
                    Error.Validation("GoogleAuth.InvalidToken", "Invalid Google ID token."));
            }

            if (!payload.EmailVerified)
            {
                return Result.Failure<GoogleIdentity>(Error.Validation(
                    "GoogleAuth.EmailNotVerified",
                    "The Google account email address is not verified."));
            }

            return new GoogleIdentity(
                payload.Subject,
                payload.Email,
                payload.EmailVerified,
                payload.GivenName,
                payload.FamilyName,
                payload.Picture);
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning(ex, "Google ID token validation failed");
            return Result.Failure<GoogleIdentity>(
                Error.Validation("GoogleAuth.InvalidToken", "Invalid or expired Google ID token."));
        }
    }
}
