using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ECommerce.Infrastructure.Email;

public class SmtpEmailService(
    IOptions<EmailSettings> options,
    ILogger<SmtpEmailService> logger) : IEmailService
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
        if (string.IsNullOrWhiteSpace(_settings.SmtpServer) ||
            string.IsNullOrWhiteSpace(_settings.SenderEmail))
        {
            // A missing configuration is a failure, not a successful "send".
            // The outbox processor will retry it and eventually dead-letter it.
            return Result.Failure(Error.Failure(
                "Email.NotConfigured",
                "SMTP server and sender address must be configured."));
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            if (!string.IsNullOrWhiteSpace(messageId))
            {
                // A deterministic Message-Id lets receiving systems suppress a
                // duplicate delivery after an ambiguous outbox failure.
                message.MessageId = BuildMessageId(messageId);
            }

            var bodyBuilder = new BodyBuilder();
            if (isHtml)
            {
                bodyBuilder.HtmlBody = body;
            }
            else
            {
                bodyBuilder.TextBody = body;
            }

            message.Body = bodyBuilder.ToMessageBody();

            using var client = new MailKit.Net.Smtp.SmtpClient();

            var socketOptions = _settings.EnableSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(_settings.SmtpServer, _settings.Port, socketOptions, ct);

            if (!string.IsNullOrWhiteSpace(_settings.Username) && !string.IsNullOrWhiteSpace(_settings.Password))
            {
                await client.AuthenticateAsync(_settings.Username, _settings.Password, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

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

    private static string BuildMessageId(string idempotencyKey)
    {
        var normalized = idempotencyKey.Trim().Trim('<', '>');
        var localPart = new string(normalized
            .Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_'
                ? character
                : '-')
            .ToArray())
            .Trim('.');

        if (string.IsNullOrWhiteSpace(localPart))
        {
            localPart = Guid.NewGuid().ToString("N");
        }

        return $"<{localPart}@outbox.ecommerce.local>";
    }
}
