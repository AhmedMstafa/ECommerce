using System.Text.Json;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Common.Email;
using FluentAssertions;

namespace ECommerce.UnitTests;

public class PasswordResetOutboxFactoryTests
{
    [Fact]
    public void Create_ShouldEncodeResetLinkAndHtmlContent()
    {
        var userId = Guid.NewGuid();

        var result = PasswordResetOutboxFactory.Create(
            "https://client.example.com/app/?ignored=1",
            "a+b@example.com",
            "<b>Ada</b>",
            userId,
            "token+/+=");

        result.IsSuccess.Should().BeTrue();
        var message = result.Value;
        message.Type.Should().Be(PasswordResetOutboxFactory.EmailMessageType);

        var payload = JsonSerializer.Deserialize<EmailOutboxPayload>(message.Content)!;
        payload.To.Should().Be("a+b@example.com");
        payload.Subject.Should().Be(EmailTemplates.PasswordResetSubject);
        payload.IsHtml.Should().BeTrue();
        payload.Body.Should().Contain("https://client.example.com/app/reset-password");
        payload.Body.Should().Contain("email=a%2Bb%40example.com");
        payload.Body.Should().Contain("token%2B%2F%2B%3D");
        payload.Body.Should().NotContain("<b>Ada</b>");
        payload.Body.Should().Contain("&lt;b&gt;Ada&lt;/b&gt;");
    }

    [Fact]
    public void Create_ShouldUseStableIdempotencyKeyPerToken()
    {
        var userId = Guid.NewGuid();

        var first = PasswordResetOutboxFactory.Create(
            "https://client.example.com", "user@example.com", "User", userId, "token-1").Value!;
        var retry = PasswordResetOutboxFactory.Create(
            "https://client.example.com", "user@example.com", "User", userId, "token-1").Value!;
        var newRequest = PasswordResetOutboxFactory.Create(
            "https://client.example.com", "user@example.com", "User", userId, "token-2").Value!;

        first.IdempotencyKey.Should().Be(retry.IdempotencyKey);
        first.IdempotencyKey.Should().NotBe(newRequest.IdempotencyKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://client.example.com")]
    public void Create_ShouldRejectInvalidClientBaseUrl(string baseUrl)
    {
        var result = PasswordResetOutboxFactory.Create(
            baseUrl, "user@example.com", "User", Guid.NewGuid(), "token");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("PasswordReset.ClientNotConfigured");
    }
}
