using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace ECommerce.Infrastructure.Payments;

public sealed class StripePaymentService : IPaymentService
{
    private readonly StripeSettings _settings;
    private readonly ILogger<StripePaymentService> _logger;
    private readonly PaymentIntentService _paymentIntentService;
    private readonly RefundService _refundService;

    public StripePaymentService(IOptions<StripeSettings> options, ILogger<StripePaymentService> logger)
    {
        _settings = options.Value;
        _logger = logger;
        var client = new StripeClient(_settings.SecretKey);
        _paymentIntentService = new PaymentIntentService(client);
        _refundService = new RefundService(client);
    }

    public async Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(
        Guid orderId,
        long amountInCents,
        string currency,
        string idempotencyKey,
        Dictionary<string, string> metadata,
        CancellationToken ct = default)
    {
        try
        {
            var options = new PaymentIntentCreateOptions
            {
                Amount = amountInCents,
                Currency = currency,
                Metadata = metadata,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                    AllowRedirects = "never"
                }
            };

            var requestOptions = new RequestOptions { IdempotencyKey = idempotencyKey };
            var intent = await _paymentIntentService.CreateAsync(options, requestOptions, ct);

            return Result.Success(new PaymentIntentResult(intent.Id, intent.ClientSecret ?? string.Empty, intent.Status));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe PaymentIntent creation failed for order {OrderId}. StripeError: {Code}", orderId, ex.StripeError?.Code);
            return Result.Failure<PaymentIntentResult>(Error.Failure("Payment.StripeError", "Payment provider rejected the request."));
        }
    }

    public async Task<Result> CancelPaymentIntentAsync(
        string paymentIntentId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        try
        {
            var requestOptions = new RequestOptions { IdempotencyKey = idempotencyKey };
            await _paymentIntentService.CancelAsync(paymentIntentId, new PaymentIntentCancelOptions(), requestOptions, ct);
            return Result.Success();
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "payment_intent_unexpected_state")
        {
            // Check current status - if already succeeded, caller must not release stock.
            try
            {
                var pi = await _paymentIntentService.GetAsync(paymentIntentId, cancellationToken: ct);
                if (string.Equals(pi.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure(Error.Conflict("Payment.AlreadySucceeded", "The payment intent has already succeeded."));
                }
                if (string.Equals(pi.Status, "canceled", StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Success();
                }
            }
            catch (StripeException innerEx)
            {
                _logger.LogWarning(innerEx, "Failed to fetch PaymentIntent status after cancel conflict for {PaymentIntentId}", paymentIntentId);
            }

            _logger.LogWarning(ex, "Stripe PaymentIntent cancel unexpected state for {PaymentIntentId}", paymentIntentId);
            return Result.Failure(Error.Conflict("Payment.CannotCancel", "The payment intent cannot be cancelled in its current state."));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe PaymentIntent cancel failed for {PaymentIntentId}. Code: {Code}", paymentIntentId, ex.StripeError?.Code);
            return Result.Failure(Error.Failure("Payment.StripeError", "Payment provider request failed."));
        }
    }

    public async Task<Result<RefundResult>> CreateRefundAsync(
        string paymentIntentId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        try
        {
            var options = new RefundCreateOptions
            {
                PaymentIntent = paymentIntentId
            };
            var requestOptions = new RequestOptions { IdempotencyKey = idempotencyKey };
            var refund = await _refundService.CreateAsync(options, requestOptions, ct);
            return Result.Success(new RefundResult(refund.Id, refund.Status ?? "unknown"));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe refund creation failed for {PaymentIntentId}. Code: {Code}", paymentIntentId, ex.StripeError?.Code);
            return Result.Failure<RefundResult>(Error.Failure("Payment.RefundFailed", "Payment provider refund request failed."));
        }
    }

    public async Task<Result<string>> GetPaymentIntentStatusAsync(string paymentIntentId, CancellationToken ct = default)
    {
        try
        {
            var pi = await _paymentIntentService.GetAsync(paymentIntentId, cancellationToken: ct);
            return Result.Success(pi.Status ?? "unknown");
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe PaymentIntent status lookup failed for {PaymentIntentId}", paymentIntentId);
            return Result.Failure<string>(Error.Failure("Payment.StripeError", "Payment provider request failed."));
        }
    }

    public async Task<Result<PaymentIntentResult>> GetPaymentIntentAsync(string paymentIntentId, CancellationToken ct = default)
    {
        try
        {
            var pi = await _paymentIntentService.GetAsync(paymentIntentId, cancellationToken: ct);
            return Result.Success(new PaymentIntentResult(pi.Id, pi.ClientSecret ?? string.Empty, pi.Status ?? "unknown"));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe PaymentIntent lookup failed for {PaymentIntentId}", paymentIntentId);
            return Result.Failure<PaymentIntentResult>(Error.Failure("Payment.StripeError", "Payment provider request failed."));
        }
    }

    public Result<StripeWebhookPayload> VerifyWebhook(string json, string signatureHeader)
    {
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, _settings.WebhookSecret);
            return Result.Success(MapEvent(stripeEvent));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            return Result.Failure<StripeWebhookPayload>(Error.Validation("Payment.InvalidWebhookSignature", "Invalid webhook signature."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stripe webhook parsing failed.");
            return Result.Failure<StripeWebhookPayload>(Error.Validation("Payment.InvalidWebhook", "Invalid webhook payload."));
        }
    }

    private static StripeWebhookPayload MapEvent(Event stripeEvent)
    {
        string? paymentIntentId = null;
        string? orderId = null;
        string? refundId = null;
        string? refundStatus = null;
        string? piStatus = null;
        string? failureMessage = null;

        if (stripeEvent.Data.Object is PaymentIntent pi)
        {
            paymentIntentId = pi.Id;
            piStatus = pi.Status;
            orderId = TryGetMetadata(pi.Metadata, "orderId");
            failureMessage = pi.LastPaymentError?.Message;
        }
        else if (stripeEvent.Data.Object is Charge charge)
        {
            paymentIntentId = charge.PaymentIntentId;
            orderId = TryGetMetadata(charge.Metadata, "orderId");
            refundStatus = charge.Refunded ? "succeeded" : null;
            if (charge.Refunds?.Data?.Count > 0)
            {
                var latestRefund = charge.Refunds.Data.OrderByDescending(r => r.Created).First();
                refundId = latestRefund.Id;
                refundStatus = latestRefund.Status;
            }
        }
        else if (stripeEvent.Data.Object is Refund refund)
        {
            paymentIntentId = refund.PaymentIntentId;
            refundId = refund.Id;
            refundStatus = refund.Status;
        }

        return new StripeWebhookPayload(
            stripeEvent.Id,
            stripeEvent.Type,
            paymentIntentId,
            orderId,
            refundId,
            refundStatus,
            piStatus,
            failureMessage);
    }

    private static string? TryGetMetadata(IReadOnlyDictionary<string, string>? metadata, string key)
    {
        if (metadata is null) return null;
        return metadata.TryGetValue(key, out var value) ? value : null;
    }
}
