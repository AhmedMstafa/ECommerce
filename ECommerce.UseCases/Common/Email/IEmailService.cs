using ECommerce.Domain.Common;

namespace ECommerce.UseCases.Common.Email;

public interface IEmailService
{
    Task<Result> SendEmailAsync(
        string to,
        string subject,
        string body,
        bool isHtml = true,
        CancellationToken ct = default,
        string? messageId = null);
}
