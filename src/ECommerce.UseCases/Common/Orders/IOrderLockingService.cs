namespace ECommerce.UseCases.Common.Orders;

public interface IOrderLockingService
{
    Task LockOrderAndReservationsAsync(Guid orderId, CancellationToken ct = default);
}
