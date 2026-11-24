using System.Security.Cryptography;
using System.Text;

namespace ECommerce.UseCases.Common.Baskets;

/// <summary>
/// Opaque, server-generated guest identity. The raw token is only transported
/// to the client; Redis keys always use the one-way hash.
/// </summary>
public static class GuestToken
{
    public const string HeaderName = "X-Guest-Token";
    public const string ResponseHeaderName = "X-Guest-Token";

    private const int TokenByteLength = 32;

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool IsValid(string? token) =>
        !string.IsNullOrWhiteSpace(token) &&
        token.Length is >= 32 and <= 64 &&
        token.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    public static string Hash(string token)
    {
        if (!IsValid(token))
        {
            throw new ArgumentException("A valid guest token is required.", nameof(token));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))
            .ToLowerInvariant();
    }
}
