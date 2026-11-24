using ECommerce.Domain.Enums;

namespace ECommerce.API.Contracts.Orders;

public sealed record UpdateOrderFulfillmentRequest(OrderStatus Status);