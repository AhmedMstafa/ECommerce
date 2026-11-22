using ECommerce.Domain.Common;

namespace ECommerce.Domain.Entities;

public sealed class RefreshToken : BaseEntity
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public string? ReasonRevoked { get; private set; }

    public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresAtUtc;
    public bool IsRevoked => RevokedAtUtc is not null;
    public bool IsActive(DateTime utcNow) => !IsRevoked && !IsExpired(utcNow);

    private RefreshToken() { }

    private RefreshToken(Guid id, Guid userId, string tokenHash, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        Id = id == default ? Guid.NewGuid() : id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        CreatedAt = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        UpdatedAt = new DateTimeOffset(createdAtUtc, TimeSpan.Zero);
    }

    public static Result<RefreshToken> Create(Guid userId, string tokenHash, DateTime createdAtUtc, DateTime expiresAtUtc, Guid id = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<RefreshToken>(Error.Validation("RefreshToken.InvalidUser", "A valid user is required."));

        if (string.IsNullOrWhiteSpace(tokenHash))
            return Result.Failure<RefreshToken>(Error.Validation("RefreshToken.InvalidToken", "A refresh token is required."));

        if (expiresAtUtc <= createdAtUtc)
            return Result.Failure<RefreshToken>(Error.Validation("RefreshToken.InvalidExpiry", "Refresh token expiry must be after creation."));

        return new RefreshToken(id, userId, tokenHash.Trim(), createdAtUtc, expiresAtUtc);
    }

    public void Revoke(DateTime utcNow, string? reason = null, string? replacedByTokenHash = null)
    {
        if (RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = utcNow;
        ReasonRevoked = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ReplacedByTokenHash = string.IsNullOrWhiteSpace(replacedByTokenHash) ? null : replacedByTokenHash.Trim();
        UpdatedAt = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }
}
