namespace ECommerce.Infrastructure.Auth;

/// <summary>
/// Settings required to validate Google ID tokens.
/// Only the client ID is needed because clients send an ID token, not an access token.
/// </summary>
public sealed class GoogleAuthSettings
{
    public const string SectionName = "GoogleAuth";

    public string ClientId { get; set; } = string.Empty;
}
