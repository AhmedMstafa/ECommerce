using ECommerce.Domain.Common;

namespace ECommerce.UseCases.Common.Payments;

public interface IPaymentService
{
    Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(
        Guid orderId,
        long amountInCents,
        string currency,
        string idempotencyKey,
        Dictionary<string, string> metadata,
        CancellationToken ct = default);

    Task<Result> CancelPaymentIntentAsync(
        string paymentIntentId,
        string idempotencyKey,
        CancellationToken ct = default);

    Task<Result<RefundResult>> CreateRefundAsync(
        string paymentIntentId,
        string idempotencyKey,
        CancellationToken ct = default);

    Task<Result<string>> GetPaymentIntentStatusAsync(
        string paymentIntentId,
        CancellationToken ct = default);

    Task<Result<PaymentIntentResult>> GetPaymentIntentAsync(
        string paymentIntentId,
        CancellationToken ct = default);

    Result<StripeWebhookPayload> VerifyWebhook(string json, string signatureHeader);
}

public sealed record PaymentIntentResult(string PaymentIntentId, string ClientSecret, string Status);
public sealed record RefundResult(string RefundId, string Status);
public sealed record StripeWebhookPayload(
    string EventId,
    string EventType,
    string? PaymentIntentId,
    string? OrderId,
    string? RefundId,
    string? RefundStatus,
    string? PaymentIntentStatus,
    string? FailureMessage);
