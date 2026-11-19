using ECommerce.Infrastructure.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerce.UnitTests;

public class EmailFailureTests
{
    [Fact]
    public async Task SmtpEmailService_ShouldReturnFailure_WhenSmtpIsNotConfigured()
    {
        var service = new SmtpEmailService(
            Options.Create(new EmailSettings
            {
                SmtpServer = string.Empty,
                SenderEmail = string.Empty
            }),
            NullLogger<SmtpEmailService>.Instance);

        var result = await service.SendEmailAsync(
            "user@example.com",
            "Subject",
            "<p>Body</p>",
            true,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Email.NotConfigured");
    }
}
