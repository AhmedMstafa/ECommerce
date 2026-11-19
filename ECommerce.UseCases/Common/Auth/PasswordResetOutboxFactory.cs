using System.Text.Json;
using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.UseCases.Common.Email;

namespace ECommerce.UseCases.Common.Auth;

/// <summary>
/// Builds the password-reset outbox message from application rules. Identity
/// infrastructure remains responsible for generating the token and persisting
/// the row; it no longer decides email content or idempotency semantics.
/// </summary>
public static class PasswordResetOutboxFactory
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
                "PasswordReset.ClientNotConfigured",
                "Password reset is temporarily unavailable."));
        }

        if (string.IsNullOrWhiteSpace(recipientEmail) || string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure<OutboxMessage>(Error.Validation(
                "PasswordReset.InvalidRequest",
                "A recipient and reset token are required."));
        }

        var origin = baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var resetLink = $"{origin}/reset-password" +
                        $"?email={Uri.EscapeDataString(recipientEmail)}" +
                        $"&token={Uri.EscapeDataString(token)}";

        var payload = new EmailOutboxPayload
        {
            To = recipientEmail,
            Subject = EmailTemplates.PasswordResetSubject,
            Body = EmailTemplates.RenderPasswordReset(recipientName, resetLink),
            IsHtml = true
        };

        // A new token means a new logical email. Retrying the same request keeps
        // the same key, so the outbox's unique index rejects a duplicate row.
        var idempotencyKey = $"password-reset:{userId}:{token}";

        return OutboxMessage.Create(
            EmailMessageType,
            JsonSerializer.Serialize(payload),
            idempotencyKey: idempotencyKey);
    }
}
