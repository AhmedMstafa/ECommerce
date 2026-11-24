namespace ECommerce.UseCases.Common.Orders;

public interface IStockAtomicService
{
    Task<int> TryDecrementAsync(Guid productId, int quantity, CancellationToken ct = default);
    Task<int> IncrementAsync(Guid productId, int quantity, CancellationToken ct = default);
}
