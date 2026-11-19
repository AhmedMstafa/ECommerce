using System.Text.Json;
using ECommerce.UseCases.Common.Email;

namespace ECommerce.Infrastructure.BackgroundJobs;

public class EmailOutboxPayload
{
    public string To { get; set; } = default!;
    public string Subject { get; set; } = default!;
    public string Body { get; set; } = default!;
    public bool IsHtml { get; set; } = true;
}

public class EmailOutboxMessageHandler(IEmailService emailService) : IOutboxMessageHandler
{
    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var emailPayload = JsonSerializer.Deserialize<EmailOutboxPayload>(payload);
        if (emailPayload is null)
        {
            throw new InvalidOperationException("Invalid email payload in Outbox Message.");
        }

        var result = await emailService.SendEmailAsync(
            emailPayload.To, 
            emailPayload.Subject, 
            emailPayload.Body, 
            emailPayload.IsHtml, 
            cancellationToken);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error?.Message ?? "Failed to send email");
        }
    }
}
