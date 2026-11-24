using ECommerce.UseCases.Auth.Commands.Register;
using ECommerce.UseCases.Auth.Commands.ResetPassword;
using ECommerce.UseCases.Common.Auth;
using FluentAssertions;

namespace ECommerce.UnitTests;

public class PasswordPolicyTests
{
    [Fact]
    public void RegisterValidator_ShouldAcceptStrongPassword()
    {
        var validator = new RegisterCommandValidator();

        var result = validator.Validate(new RegisterCommand(
            "user@example.com",
            "Str0ng!Password",
            "Test",
            "User"));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("short1!")]
    [InlineData("lowercase1!")]
    [InlineData("UPPERCASE1!")]
    [InlineData("NoDigit!!")]
    [InlineData("NoSpecial123")]
    public void RegisterValidator_ShouldRejectWeakPasswords(string password)
    {
        var validator = new RegisterCommandValidator();

        var result = validator.Validate(new RegisterCommand(
            "user@example.com",
            password,
            "Test",
            "User"));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ResetPasswordValidator_ShouldUseTheSameStrongPolicy()
    {
        var validator = new ResetPasswordCommandValidator();

        var weak = validator.Validate(new ResetPasswordCommand("user@example.com", "token", "weak"));
        var strong = validator.Validate(new ResetPasswordCommand("user@example.com", "token", "Str0ng!Password"));

        weak.IsValid.Should().BeFalse();
        strong.IsValid.Should().BeTrue();
        PasswordValidationExtensions.RequiredLength.Should().Be(8);
    }
}
