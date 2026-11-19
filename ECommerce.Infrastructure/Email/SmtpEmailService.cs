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
        CancellationToken ct = default)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

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

            // Try connecting to SMTP server if configured
            if (!string.IsNullOrWhiteSpace(_settings.SmtpServer) && _settings.SmtpServer != "localhost")
            {
                await client.ConnectAsync(_settings.SmtpServer, _settings.Port, 
                    _settings.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);

                if (!string.IsNullOrWhiteSpace(_settings.Username) && !string.IsNullOrWhiteSpace(_settings.Password))
                {
                    await client.AuthenticateAsync(_settings.Username, _settings.Password, ct);
                }

                await client.SendAsync(message, ct);
                await client.DisconnectAsync(true, ct);
            }
            else
            {
                // In local dev without live SMTP, log the email body
                logger.LogInformation("[EMAIL DISPATCHED] To: {To} | Subject: {Subject} | Body length: {Length}", 
                    to, subject, body.Length);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {To}", to);
            return Result.Failure(Error.Failure("Email.SendFailed", $"Failed to send email: {ex.Message}"));
        }
    }
}
