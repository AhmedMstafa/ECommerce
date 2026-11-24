using ECommerce.Infrastructure.Persistence;
using ECommerce.UseCases.Common.Orders;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Infrastructure.Orders;

public sealed class OrderLockingService(StoreDbContext dbContext) : IOrderLockingService
{
    public async Task LockOrderAndReservationsAsync(Guid orderId, CancellationToken ct = default)
    {
        // Lock order row and reservation rows in the current transaction to prevent race conditions.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM Orders WITH (UPDLOCK, ROWLOCK) WHERE Id = {orderId}", ct);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM StockReservations WITH (UPDLOCK, ROWLOCK) WHERE OrderId = {orderId}", ct);
    }
}
