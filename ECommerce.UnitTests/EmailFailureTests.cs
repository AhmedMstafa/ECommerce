using ECommerce.Domain.Common;
using ECommerce.Infrastructure.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Resend;

namespace ECommerce.UnitTests;

public class EmailFailureTests
{
    [Fact]
    public async Task ResendEmailService_ShouldReturnFailure_WhenApiKeyIsNotConfigured()
    {
        var service = CreateService(new EmailSettings
        {
            ApiKey = string.Empty,
            SenderEmail = "noreply@example.com"
        });

        var result = await SendAsync(service);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Email.NotConfigured");
    }

    [Fact]
    public async Task ResendEmailService_ShouldReturnFailure_WhenSenderIsNotConfigured()
    {
        var service = CreateService(new EmailSettings
        {
            ApiKey = "test-value",
            SenderEmail = string.Empty
        });

        var result = await SendAsync(service);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Email.NotConfigured");
    }

    private static ResendEmailService CreateService(EmailSettings settings) =>
        new(
            Options.Create(settings),
            Substitute.For<IResend>(),
            NullLogger<ResendEmailService>.Instance);

    private static Task<Result> SendAsync(ResendEmailService service) =>
        service.SendEmailAsync(
            "user@example.com",
            "Subject",
            "<p>Body</p>",
            true,
            CancellationToken.None);
}
