using FluentValidation;

namespace ECommerce.UseCases.Common.Auth;

/// <summary>
/// Shared password rules for application-level validation. The values mirror
/// the ASP.NET Core Identity policy configured in Infrastructure so callers
/// receive a validation error before an Identity error for the same problem.
/// </summary>
public static class PasswordValidationExtensions
{
    public const int RequiredLength = 8;

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(
        this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(RequiredLength)
            .WithMessage($"Password must be at least {RequiredLength} characters long.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");
    }
}
