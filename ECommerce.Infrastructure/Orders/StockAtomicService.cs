using ECommerce.Infrastructure.Persistence;
using ECommerce.UseCases.Common.Orders;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Infrastructure.Orders;

public sealed class StockAtomicService(StoreDbContext dbContext) : IStockAtomicService
{
    public async Task<int> TryDecrementAsync(Guid productId, int quantity, CancellationToken ct = default)
    {
        // Atomic conditional decrement - prevents overselling.
        return await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Products SET StockQuantity = StockQuantity - {quantity}, UpdatedAt = {DateTimeOffset.UtcNow} WHERE Id = {productId} AND StockQuantity >= {quantity} AND IsDeleted = 0",
            ct);
    }

    public async Task<int> IncrementAsync(Guid productId, int quantity, CancellationToken ct = default)
    {
        return await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Products SET StockQuantity = StockQuantity + {quantity}, UpdatedAt = {DateTimeOffset.UtcNow} WHERE Id = {productId}",
            ct);
    }
}
