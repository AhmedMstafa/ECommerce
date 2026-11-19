using ECommerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.BackgroundJobs;

public class OutboxProcessorJob(
    StoreDbContext dbContext,
    IServiceProvider serviceProvider,
    ILogger<OutboxProcessorJob> logger)
{
    public async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken = default)
    {
        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null && m.RetryCount < 5)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return;
        }

        logger.LogInformation("Processing {Count} pending outbox messages...", messages.Count);

        foreach (var message in messages)
        {
            try
            {
                logger.LogInformation("Processing Outbox Message {Id} of type {Type}", message.Id, message.Type);

                // Extensible message handling via scoped handlers / services
                using var scope = serviceProvider.CreateScope();
                
                // When we implement Email in Phase 4, the handler processes Email messages here
                var handler = scope.ServiceProvider.GetKeyedService<IOutboxMessageHandler>(message.Type);
                if (handler is not null)
                {
                    await handler.HandleAsync(message.Content, cancellationToken);
                }

                message.MarkAsProcessed();
                logger.LogInformation("Outbox Message {Id} processed successfully", message.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process Outbox Message {Id}. Attempt {Attempt}", 
                    message.Id, message.RetryCount + 1);

                message.MarkAsFailed(ex.Message);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public interface IOutboxMessageHandler
{
    Task HandleAsync(string payload, CancellationToken cancellationToken);
}
