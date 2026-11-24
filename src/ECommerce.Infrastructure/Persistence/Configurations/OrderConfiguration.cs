using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired();
        builder.Property(x => x.DeliveryMethodId).IsRequired();
        builder.Property(x => x.DeliveryMethodNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(x => x.DeliveryMethodPriceSnapshot).HasPrecision(18, 2);
        builder.Property(x => x.Subtotal).HasPrecision(18, 2);
        builder.Property(x => x.DeliveryFee).HasPrecision(18, 2);
        builder.Property(x => x.Total).HasPrecision(18, 2);
        builder.Property(x => x.OrderStatus).IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.PaymentStatus).IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.StripePaymentIntentId).HasMaxLength(255);
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(x => x.ReservationExpiresAtUtc).IsRequired();
        builder.Property(x => x.RequiresReconciliation).IsRequired();
        builder.Property(x => x.ReconciliationReason).HasMaxLength(500);
        builder.Property(x => x.RefundId).HasMaxLength(255);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.OwnsOne(x => x.ShippingAddressSnapshot, sa =>
        {
            sa.Property(a => a.FirstName).IsRequired().HasMaxLength(100).HasColumnName("ShippingFirstName");
            sa.Property(a => a.LastName).IsRequired().HasMaxLength(100).HasColumnName("ShippingLastName");
            sa.Property(a => a.Street).IsRequired().HasMaxLength(256).HasColumnName("ShippingStreet");
            sa.Property(a => a.City).IsRequired().HasMaxLength(100).HasColumnName("ShippingCity");
            sa.Property(a => a.State).IsRequired().HasMaxLength(100).HasColumnName("ShippingState");
            sa.Property(a => a.ZipCode).IsRequired().HasMaxLength(20).HasColumnName("ShippingZipCode");
            sa.Property(a => a.Country).IsRequired().HasMaxLength(100).HasColumnName("ShippingCountry");
        });

        builder.Navigation(x => x.ShippingAddressSnapshot).IsRequired();

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.StockReservations)
            .WithOne()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DeliveryMethod>()
            .WithMany()
            .HasForeignKey(x => x.DeliveryMethodId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.BuyerId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => x.BuyerId);
        builder.HasIndex(x => x.OrderStatus);
        builder.HasIndex(x => x.PaymentStatus);
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => x.StripePaymentIntentId)
            .IsUnique()
            .HasFilter("[StripePaymentIntentId] IS NOT NULL");
        builder.HasIndex(x => x.RequiresReconciliation);
    }
}
