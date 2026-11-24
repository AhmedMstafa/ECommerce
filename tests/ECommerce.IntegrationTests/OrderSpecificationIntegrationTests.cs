using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.Infrastructure.BackgroundJobs;
using ECommerce.UseCases.Orders.Commands.CreateOrder;
using ECommerce.UseCases.Orders.Commands.ProcessStripeWebhook;
using ECommerce.UseCases.Orders.Queries.GetMyOrders;
using ECommerce.UseCases.Orders.Queries.GetOrdersForAdmin;
using ECommerce.UseCases.Orders.Queries.GetReconciliationOrders;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class OrderSpecificationIntegrationTests(IntegrationTestFixture fixture)
{
    /// <summary>
    /// These specifications used AddInclude(x =&gt; (object)x.Items), which EF Core 10
    /// rejects inside Include. Executing them against real SQL Server catches the
    /// regression at query compilation time even when no orders exist yet.
    /// </summary>
    [Fact]
    public async Task Order_specs_with_collection_includes_execute_against_sql_server()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = uow.AuditedReadRepository<Order>();
        var buyerId = Guid.NewGuid();

        _ = await repository.GetEntityWithSpecAsync(
            new OrderByBuyerAndIdempotencyKeySpec(buyerId, "integration-test-key"));
        _ = await repository.GetEntityWithSpecAsync(
            new OrderByIdWithDetailsSpec(Guid.NewGuid()));
        _ = await repository.GetEntityWithSpecAsync(
            new OrderByPaymentIntentIdSpec("pi_integration_test_missing"));
        _ = await repository.GetEntityWithSpecAsync(
            new OrderWithReservationsSpec(Guid.NewGuid()));

        _ = await repository.GetAllWithSpecAsync(new OrdersByBuyerSpec(buyerId));
        _ = await repository.GetAllWithSpecAsync(new AdminOrdersSpec(null, null, null));
        _ = await repository.GetAllWithSpecAsync(new ReconciliationOrdersSpec());
    }
}
