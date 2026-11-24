using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resend;

namespace ECommerce.Infrastructure.Email;

public class ResendEmailService(
    IOptions<EmailSettings> options,
    IResend resend,
    ILogger<ResendEmailService> logger) : IEmailService
{
    private readonly EmailSettings _settings = options.Value;

    public async Task<Result> SendEmailAsync(
        string to,
        string subject,
        string body,
        bool isHtml = true,
        CancellationToken ct = default,
        string? messageId = null)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) ||
            string.IsNullOrWhiteSpace(_settings.SenderEmail))
        {
            // A missing configuration is a failure, not a successful "send".
            // The outbox processor will retry it and eventually dead-letter it.
            return Result.Failure(Error.Failure(
                "Email.NotConfigured",
                "Resend API key and sender address must be configured."));
        }

        try
        {
            var message = new EmailMessage
            {
                From = BuildFrom(_settings.SenderName, _settings.SenderEmail),
                Subject = subject
            };

            message.To.Add(to);

            if (isHtml)
            {
                message.HtmlBody = body;
            }
            else
            {
                message.TextBody = body;
            }

            // The outbox idempotency key is stable across retries. Resend keeps
            // API idempotency keys for 24 hours, which covers the outbox retry
            // window and prevents an ambiguous failure from sending twice.
            var response = string.IsNullOrWhiteSpace(messageId)
                ? await resend.EmailSendAsync(message, ct)
                : await resend.EmailSendAsync(messageId, message, ct);

            if (!response.Success)
            {
                logger.LogError(
                    "Resend rejected email with subject {Subject}. Error type: {ErrorType}, status: {StatusCode}",
                    subject,
                    response.Exception?.ErrorType,
                    response.Exception?.StatusCode);

                return Result.Failure(Error.Failure("Email.SendFailed", "Failed to send email."));
            }

            logger.LogInformation("Email sent with subject {Subject}", subject);

            return Result.Success();
        }
        catch (Exception ex)
        {
            // Do not log the recipient address: it is personal data.
            logger.LogError(ex, "Failed to send email with subject {Subject}", subject);
            return Result.Failure(Error.Failure("Email.SendFailed", "Failed to send email."));
        }
    }

    private static string BuildFrom(string? senderName, string senderEmail)
    {
        var address = senderEmail.Trim();

        if (string.IsNullOrWhiteSpace(senderName))
        {
            return address;
        }

        // The configuration value is trusted, but keep angle brackets out of
        // the friendly-name portion so the Resend payload stays well-formed.
        var name = senderName.Trim().Replace("<", string.Empty).Replace(">", string.Empty);

        if (string.IsNullOrWhiteSpace(name))
        {
            return address;
        }

        return $"{name} <{address}>";
    }
}
