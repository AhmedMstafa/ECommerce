using ECommerce.Domain.Entities;

namespace ECommerce.Infrastructure.BackgroundJobs;

public interface IOutboxMessageHandler
{
    Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken);
}
