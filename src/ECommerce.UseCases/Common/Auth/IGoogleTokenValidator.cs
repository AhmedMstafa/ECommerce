using ECommerce.Domain.Common;

namespace ECommerce.UseCases.Common.Auth;

/// <summary>
/// The normalized identity returned after a Google ID token has been verified.
/// The Google SDK type is deliberately kept out of the application layer.
/// </summary>
public sealed record GoogleIdentity(
    string Subject,
    string Email,
    bool EmailVerified,
    string? FirstName,
    string? LastName,
    string? Picture);

/// <summary>
/// Validates Google ID tokens. Kept as an abstraction so authentication rules
/// can be unit tested without network calls or a real Google client.
/// </summary>
public interface IGoogleTokenValidator
{
    Task<Result<GoogleIdentity>> ValidateAsync(string idToken, CancellationToken ct = default);
}
