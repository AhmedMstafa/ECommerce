using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.Common.Payments;
using ECommerce.UseCases.Orders.Commands.CreateOrder;
using ECommerce.UseCases.Orders.Dtos;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Orders.Commands.RetryPayment;

public sealed record RetryPaymentCommand(Guid BuyerId, Guid OrderId) : IRequest<Result<OrderResponse>>;

public sealed class RetryPaymentCommandValidator : AbstractValidator<RetryPaymentCommand>
{
    public RetryPaymentCommandValidator()
    {
        RuleFor(x => x.BuyerId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
    }
}

public sealed class RetryPaymentCommandHandler(
    IUnitOfWork uow,
    IPaymentService paymentService)
    : IRequestHandler<RetryPaymentCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(RetryPaymentCommand request, CancellationToken ct)
    {
        var order = await uow.AuditedReadRepository<Order>()
            .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(request.OrderId), ct);
        if (order is null)
            return Result.Failure<OrderResponse>(OrderErrors.NotFound(request.OrderId));
        if (order.BuyerId != request.BuyerId)
            return Result.Failure<OrderResponse>(OrderErrors.NotOwner);

        if (order.OrderStatus == OrderStatus.Cancelled ||
            order.PaymentStatus is PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.Refunded)
        {
            return Result.Failure<OrderResponse>(Error.Conflict("Order.RetryNotAllowed",
                "This order cannot be retried. Please create a new order."));
        }

        if (order.ReservationExpiresAtUtc <= DateTime.UtcNow)
        {
            return Result.Failure<OrderResponse>(Error.Conflict("Order.ReservationExpired",
                "The order reservation has expired. Please create a new order."));
        }

        if (string.IsNullOrWhiteSpace(order.StripePaymentIntentId))
        {
            return Result.Failure<OrderResponse>(Error.Conflict("Order.NoPaymentIntent",
                "No payment intent exists for this order. Please create a new order."));
        }

        var piResult = await paymentService.GetPaymentIntentAsync(order.StripePaymentIntentId, ct);
        if (piResult.IsFailure)
            return Result.Failure<OrderResponse>(piResult.Error!);

        var status = piResult.Value.Status;
        var retryable = status is "requires_payment_method" or "requires_confirmation" or "requires_action";
        if (!retryable)
        {
            return Result.Failure<OrderResponse>(Error.Conflict("Order.RetryNotAllowed",
                "This payment can no longer be retried on the same order. Please create a new order."));
        }

        return Result.Success(CreateOrderCommandHandler.MapOrder(order, piResult.Value.ClientSecret));
    }
}
