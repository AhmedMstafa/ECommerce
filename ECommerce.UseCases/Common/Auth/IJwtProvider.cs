namespace ECommerce.UseCases.Common.Auth;

public interface IJwtProvider
{
    string GenerateToken(Guid userId, string email, string fullName, IEnumerable<string> roles);
}

public record AuthResponse(
    string Token,
    string Email,
    string FullName,
    DateTime ExpiresAt);
