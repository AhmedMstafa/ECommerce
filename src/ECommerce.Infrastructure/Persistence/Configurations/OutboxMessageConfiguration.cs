using ECommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.Property(x => x.Type)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(x => x.Content)
            .IsRequired();

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(OutboxMessage.ErrorMessageMaxLength);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(OutboxMessage.IdempotencyKeyMaxLength);

        builder.Property(x => x.LeaseOwner)
            .HasMaxLength(200);

        // One stable key per logical message. The unique index turns a retry of
        // the same operation into a constraint violation rather than a duplicate.
        builder.HasIndex(x => x.IdempotencyKey)
            .IsUnique();

        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.OccurredOnUtc });
        builder.HasIndex(x => x.LeaseOwner);
        builder.HasIndex(x => new { x.ProcessedOnUtc, x.RetryCount });
    }
}
