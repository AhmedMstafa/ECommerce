using ECommerce.Domain.Common;
using ECommerce.Infrastructure.Auth;
using ECommerce.UseCases.Common.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerce.UnitTests;

public class GoogleSecurityTests
{
    [Fact]
    public async Task GoogleTokenValidator_ShouldRejectToken_WhenClientIdIsNotConfigured()
    {
        var validator = new GoogleTokenValidator(
            Options.Create(new GoogleAuthSettings { ClientId = string.Empty }),
            NullLogger<GoogleTokenValidator>.Instance);

        var result = await validator.ValidateAsync("any-token");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("GoogleAuth.NotConfigured");
    }

    [Theory]
    [InlineData(false, false, GoogleLoginDecision.CreateNewUser)]
    [InlineData(false, true, GoogleLoginDecision.RejectUnlinkedEmailOwner)]
    [InlineData(true, false, GoogleLoginDecision.UseLinkedUser)]
    [InlineData(true, true, GoogleLoginDecision.UseLinkedUser)]
    public void DecideLogin_ShouldNeverAutoLinkByEmail(
        bool hasLinkedUser,
        bool hasEmailOwner,
        GoogleLoginDecision expected)
    {
        GoogleLinkingRules.DecideLogin(hasLinkedUser, hasEmailOwner).Should().Be(expected);
    }

    [Theory]
    [InlineData("user@example.com", "USER@EXAMPLE.COM", true)]
    [InlineData("user@example.com", "other@example.com", false)]
    [InlineData(null, "user@example.com", false)]
    public void CanExplicitlyLink_ShouldRequireMatchingVerifiedEmail(
        string? authenticatedEmail,
        string googleEmail,
        bool expected)
    {
        GoogleLinkingRules.CanExplicitlyLink(authenticatedEmail, googleEmail).Should().Be(expected);
    }

    [Fact]
    public void GoogleIdentity_ShouldCarryVerifiedEmailFlag()
    {
        var identity = new GoogleIdentity("subject", "user@example.com", true, "User", "Test", null);

        identity.EmailVerified.Should().BeTrue();
        identity.Email.Should().Be("user@example.com");
    }
}
