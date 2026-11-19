using System.Text.Json;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Email;

namespace ECommerce.Infrastructure.BackgroundJobs;

public sealed class EmailOutboxPayload
{
    public string To { get; set; } = default!;
    public string Subject { get; set; } = default!;
    public string Body { get; set; } = default!;
    public bool IsHtml { get; set; } = true;
}

public sealed class EmailOutboxMessageHandler(IEmailService emailService) : IOutboxMessageHandler
{
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        EmailOutboxPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<EmailOutboxPayload>(message.Content);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Invalid JSON in email outbox message.", ex);
        }

        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.To) ||
            string.IsNullOrWhiteSpace(payload.Subject) ||
            string.IsNullOrWhiteSpace(payload.Body))
        {
            throw new InvalidOperationException("Email outbox message is missing required fields.");
        }

        var result = await emailService.SendEmailAsync(
            payload.To,
            payload.Subject,
            payload.Body,
            payload.IsHtml,
            cancellationToken,
            message.IdempotencyKey);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error?.Message ?? "Failed to send email");
        }
    }
}
