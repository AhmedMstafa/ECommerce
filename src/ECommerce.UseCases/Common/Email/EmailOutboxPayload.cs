namespace ECommerce.UseCases.Common.Email;

/// <summary>
/// Wire contract for the keyed "Email" outbox handler. The application layer
/// owns this contract; Infrastructure only serializes and transports it.
/// </summary>
public sealed class EmailOutboxPayload
{
    public string To { get; set; } = default!;
    public string Subject { get; set; } = default!;
    public string Body { get; set; } = default!;
    public bool IsHtml { get; set; } = true;
}
