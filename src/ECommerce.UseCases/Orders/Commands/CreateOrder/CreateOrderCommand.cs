using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Repositories;
using ECommerce.Domain.Specifications;
using ECommerce.Domain.Specifications.Builders;
using ECommerce.UseCases.Common.Orders;
using ECommerce.UseCases.Common.Payments;
using ECommerce.UseCases.Orders.Dtos;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Orders.Commands.CreateOrder;

public sealed record CreateOrderCommand(Guid BuyerId, Guid AddressId, Guid DeliveryMethodId, string IdempotencyKey)
    : IRequest<Result<OrderResponse>>;

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.BuyerId).NotEmpty();
        RuleFor(x => x.AddressId).NotEmpty();
        RuleFor(x => x.DeliveryMethodId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
    }
}

public sealed class CreateOrderCommandHandler(
    IUnitOfWork uow,
    IBasketStore basketStore,
    IStockAtomicService stockAtomicService,
    IPaymentService paymentService,
    TimeProvider timeProvider)
    : IRequestHandler<CreateOrderCommand, Result<OrderResponse>>
{
    private static readonly TimeSpan ReservationDuration = TimeSpan.FromMinutes(30);

    public async Task<Result<OrderResponse>> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        if (request.BuyerId == Guid.Empty)
            return Result.Failure<OrderResponse>(Error.Unauthorized("Order.Unauthorized", "Authentication is required."));
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Result.Failure<OrderResponse>(OrderErrors.IdempotencyKeyRequired);
        if (request.IdempotencyKey.Length > 128)
            return Result.Failure<OrderResponse>(OrderErrors.IdempotencyKeyTooLong);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Idempotency: return existing order for same buyer+key.
        var existing = await uow.AuditedReadRepository<Order>()
            .GetEntityWithSpecAsync(new OrderByBuyerAndIdempotencyKeySpec(request.BuyerId, request.IdempotencyKey), ct);
        if (existing is not null)
        {
            if (existing.OrderStatus == OrderStatus.Cancelled &&
                existing.PaymentStatus is PaymentStatus.Failed or PaymentStatus.Cancelled)
            {
                return Result.Failure<OrderResponse>(Error.Conflict("Order.IdempotencyKeyReused",
                    "This idempotency key was used for a cancelled or failed order. Please create a new order."));
            }

            return Result.Success(MapOrder(existing));
        }

        // Load basket
        var basketKey = BasketKey.ForUser(request.BuyerId);
        var basket = await basketStore.GetAsync(basketKey, ct);
        if (basket is null || basket.Items.Count == 0)
            return Result.Failure<OrderResponse>(OrderErrors.EmptyBasket);

        // Load address and validate ownership
        var address = await uow.Repository<Address>().GetByIdAsync(request.AddressId, ct);
        if (address is null)
            return Result.Failure<OrderResponse>(OrderErrors.AddressNotFound);
        if (address.UserId != request.BuyerId)
            return Result.Failure<OrderResponse>(OrderErrors.AddressNotOwned);

        // Load delivery method
        var deliveryMethod = await uow.AuditedRepository<DeliveryMethod>().GetByIdAsync(request.DeliveryMethodId, ct);
        if (deliveryMethod is null)
            return Result.Failure<OrderResponse>(OrderErrors.DeliveryMethodNotFound);
        if (!deliveryMethod.IsActive)
            return Result.Failure<OrderResponse>(OrderErrors.DeliveryMethodInactive);

        // Load products from SQL
        var productIds = basket.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await uow.Repository<Product>().GetAllWithSpecAsync(new ProductsByIdsForOrderSpec(productIds), ct);
        var productById = products.ToDictionary(p => p.Id);

        // Validate basket items
        foreach (var item in basket.Items)
        {
            if (!productById.TryGetValue(item.ProductId, out var product))
                return Result.Failure<OrderResponse>(OrderErrors.ProductUnavailable);

            if (product.StockQuantity < item.Quantity)
                return Result.Failure<OrderResponse>(OrderErrors.InsufficientStock);
        }

        // Calculate totals server-side from current SQL prices
        var orderItemsData = basket.Items.Select(i =>
        {
            var p = productById[i.ProductId];
            return (p.Id, p.Name, p.PictureUrl, p.Price, i.Quantity, LineTotal: p.Price * i.Quantity);
        }).ToList();

        var subtotal = orderItemsData.Sum(i => i.LineTotal);
        var reservationExpiresAtUtc = now.Add(ReservationDuration);

        // Atomic transaction: idempotency recheck, stock decrement, create order+reservations
        var createResult = await uow.ExecuteInTransactionResultAsync(async innerCt =>
        {
            // Recheck idempotency inside tx
            var existingInTx = await uow.AuditedReadRepository<Order>()
                .GetEntityWithSpecAsync(new OrderByBuyerAndIdempotencyKeySpec(request.BuyerId, request.IdempotencyKey), innerCt);
            if (existingInTx is not null)
            {
                if (existingInTx.OrderStatus == OrderStatus.Cancelled &&
                    existingInTx.PaymentStatus is PaymentStatus.Failed or PaymentStatus.Cancelled)
                {
                    return Result.Failure<Order>(Error.Conflict("Order.IdempotencyKeyReused",
                        "This idempotency key was used for a cancelled or failed order. Please create a new order."));
                }
                return Result.Success(existingInTx);
            }

            var shippingSnapshot = OrderShippingAddress.FromAddress(address);
            var orderResult = Order.Create(
                request.BuyerId,
                shippingSnapshot,
                deliveryMethod.Id,
                deliveryMethod.Name,
                deliveryMethod.Price,
                subtotal,
                request.IdempotencyKey,
                now,
                reservationExpiresAtUtc);

            if (orderResult.IsFailure)
                return Result.Failure<Order>(orderResult.Error!);

            var order = orderResult.Value;

            // Add items
            foreach (var item in orderItemsData)
            {
                var orderItem = new OrderItem(order.Id, item.Id, item.Name, item.PictureUrl, item.Price, item.Quantity);
                order.AddItem(orderItem);
            }

            // Atomic stock decrement + create reservations
            var decremented = new List<(Guid ProductId, int Quantity)>();
            foreach (var item in orderItemsData)
            {
                var affected = await stockAtomicService.TryDecrementAsync(item.Id, item.Quantity, innerCt);
                if (affected <= 0)
                {
                    // Rollback stock decremented so far in this tx
                    foreach (var d in decremented)
                        await stockAtomicService.IncrementAsync(d.ProductId, d.Quantity, innerCt);
                    return Result.Failure<Order>(OrderErrors.InsufficientStock);
                }

                decremented.Add((item.Id, item.Quantity));
                var reservation = new StockReservation(order.Id, item.Id, item.Quantity, now, reservationExpiresAtUtc);
                order.AddReservation(reservation);
            }

            uow.AuditedRepository<Order>().Add(order);
            await uow.SaveChangesAsync(innerCt);
            return Result.Success(order);
        }, ct);

        if (createResult.IsFailure)
            return Result.Failure<OrderResponse>(createResult.Error!);

        var createdOrder = createResult.Value;

        // Existing order returned from idempotency recheck (already has PI or was previously created).
        if (!string.IsNullOrWhiteSpace(createdOrder.StripePaymentIntentId))
        {
            return Result.Success(MapOrder(createdOrder));
        }

        // Create Stripe PaymentIntent outside DB transaction
        var amountInCents = ToCents(createdOrder.Total);
        var piMetadata = new Dictionary<string, string>
        {
            ["orderId"] = createdOrder.Id.ToString(),
            ["buyerId"] = createdOrder.BuyerId.ToString()
        };

        var piResult = await paymentService.CreatePaymentIntentAsync(
            createdOrder.Id,
            amountInCents,
            "usd",
            $"order_pi_{createdOrder.Id}",
            piMetadata,
            ct);

        if (piResult.IsFailure)
        {
            // Compensate: cancel order, release reservations, restore stock
            await CompensateAsync(createdOrder.Id, ct);
            return Result.Failure<OrderResponse>(OrderErrors.PaymentIntentCreationFailed);
        }

        var pi = piResult.Value;

        // Persist PaymentIntentId
        createdOrder.SetPaymentIntentId(pi.PaymentIntentId, timeProvider.GetUtcNow().UtcDateTime);
        uow.AuditedRepository<Order>().Update(createdOrder);
        await uow.SaveChangesAsync(ct);

        var response = MapOrder(createdOrder, pi.ClientSecret);
        return Result.Success(response);
    }

    private async Task CompensateAsync(Guid orderId, CancellationToken ct)
    {
        try
        {
            await uow.ExecuteInTransactionAsync(async innerCt =>
            {
                var order = await uow.AuditedReadRepository<Order>()
                    .GetEntityWithSpecAsync(new OrderByIdWithDetailsSpec(orderId), innerCt);
                if (order is null) return;

                var now = timeProvider.GetUtcNow().UtcDateTime;

                if (order.OrderStatus != OrderStatus.Cancelled)
                {
                    order.MarkPaymentIntentFailed(now);
                }

                foreach (var reservation in order.StockReservations.Where(r => r.Status == ReservationStatus.Reserved))
                {
                    reservation.Release(now);
                    await stockAtomicService.IncrementAsync(reservation.ProductId, reservation.Quantity, innerCt);
                }

                uow.AuditedRepository<Order>().Update(order);
                await uow.SaveChangesAsync(innerCt);
            }, ct);
        }
        catch
        {
            // Compensation failure should be logged by infra/observability; order remains pending for reconciliation.
        }
    }

    private static long ToCents(decimal amount)
    {
        return (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
    }

    internal static OrderResponse MapOrder(Order order, string? clientSecret = null)
    {
        return new OrderResponse(
            order.Id,
            order.BuyerId,
            order.OrderStatus.ToString(),
            order.PaymentStatus.ToString(),
            order.Subtotal,
            order.DeliveryFee,
            order.Total,
            order.DeliveryMethodNameSnapshot,
            order.DeliveryMethodPriceSnapshot,
            new OrderShippingAddressResponse(
                order.ShippingAddressSnapshot.FirstName,
                order.ShippingAddressSnapshot.LastName,
                order.ShippingAddressSnapshot.Street,
                order.ShippingAddressSnapshot.City,
                order.ShippingAddressSnapshot.State,
                order.ShippingAddressSnapshot.ZipCode,
                order.ShippingAddressSnapshot.Country),
            order.Items.Select(i => new OrderItemResponse(i.ProductId, i.ProductName, i.PictureUrl, i.UnitPrice, i.Quantity, i.LineTotal)).ToList(),
            order.StripePaymentIntentId,
            clientSecret,
            order.RequiresReconciliation,
            order.ReconciliationReason,
            order.CreatedAtUtc,
            order.PaidAtUtc,
            order.CancelledAtUtc,
            order.RefundedAtUtc);
    }
}

public sealed class OrderByBuyerAndIdempotencyKeySpec : BaseSpecification<Order>
{
    public OrderByBuyerAndIdempotencyKeySpec(Guid buyerId, string idempotencyKey)
    {
        AddCriteria(x => x.BuyerId == buyerId && x.IdempotencyKey == idempotencyKey);
        Query.Include(x => x.Items);
        Query.Include(x => x.StockReservations);
    }
}

public sealed class OrderByIdWithDetailsSpec : BaseSpecification<Order>
{
    public OrderByIdWithDetailsSpec(Guid orderId)
    {
        AddCriteria(x => x.Id == orderId);
        Query.Include(x => x.Items);
        Query.Include(x => x.StockReservations);
        EnableTracking();
    }
}

public sealed class ProductsByIdsForOrderSpec : BaseSpecification<Product>
{
    public ProductsByIdsForOrderSpec(IReadOnlyCollection<Guid> ids)
    {
        AddCriteria(p => ids.Contains(p.Id));
        EnableTracking();
    }
}
