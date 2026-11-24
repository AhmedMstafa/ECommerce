using ECommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Infrastructure.Persistence.Configurations;

public class StripeWebhookEventConfiguration : IEntityTypeConfiguration<StripeWebhookEvent>
{
    public void Configure(EntityTypeBuilder<StripeWebhookEvent> builder)
    {
        builder.Property(x => x.StripeEventId).IsRequired().HasMaxLength(255);
        builder.Property(x => x.EventType).IsRequired().HasMaxLength(255);
        builder.Property(x => x.ProcessedAtUtc).IsRequired();
        builder.Property(x => x.ProcessingStatus).IsRequired().HasMaxLength(50);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => x.StripeEventId).IsUnique();
        builder.HasIndex(x => x.ProcessedAtUtc);
    }
}
