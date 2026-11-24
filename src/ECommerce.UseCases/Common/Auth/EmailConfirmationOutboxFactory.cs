using System.Text.Json;
using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.UseCases.Common.Email;

namespace ECommerce.UseCases.Common.Auth;

public static class EmailConfirmationOutboxFactory
{
    public const string EmailMessageType = "Email";

    public static Result<OutboxMessage> Create(
        string clientBaseUrl,
        string recipientEmail,
        string recipientName,
        Guid userId,
        string token)
    {
        if (!Uri.TryCreate(clientBaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
        {
            return Result.Failure<OutboxMessage>(Error.Failure(
                "EmailConfirmation.ClientNotConfigured",
                "Email confirmation is temporarily unavailable."));
        }

        if (string.IsNullOrWhiteSpace(recipientEmail) || string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure<OutboxMessage>(Error.Validation(
                "EmailConfirmation.InvalidRequest",
                "A recipient and confirmation token are required."));
        }

        var origin = baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var confirmationLink = $"{origin}/confirm-email" +
                               $"?userId={Uri.EscapeDataString(userId.ToString())}" +
                               $"&token={Uri.EscapeDataString(token)}";

        var payload = new EmailOutboxPayload
        {
            To = recipientEmail,
            Subject = EmailTemplates.EmailConfirmationSubject,
            Body = EmailTemplates.RenderEmailConfirmation(recipientName, confirmationLink),
            IsHtml = true
        };

        var idempotencyKey = $"email-confirmation:{userId}:{token}";
        return OutboxMessage.Create(EmailMessageType, JsonSerializer.Serialize(payload), idempotencyKey: idempotencyKey);
    }
}
