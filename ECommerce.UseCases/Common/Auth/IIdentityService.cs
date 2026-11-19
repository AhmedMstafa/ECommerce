using ECommerce.Domain.Common;

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

    Task<Result> ForgotPasswordAsync(
        string email, 
        CancellationToken ct = default);

    Task<Result> ResetPasswordAsync(
        string email, 
        string token, 
        string newPassword, 
        CancellationToken ct = default);

    Task<Result<AuthResponse>> GoogleLoginAsync(
        string idToken, 
        CancellationToken ct = default);
}
