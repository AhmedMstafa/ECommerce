using ECommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(x => x.PictureUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Price)
            .HasPrecision(18, 2);

        builder.Property(x => x.StockQuantity)
            .HasDefaultValue(0);

        // The inverse navigations are configured here as well, so EF Core does
        // not create a second shadow relationship (ProductBrandId1).
        // Keep the legacy column as a scalar shadow property: existing databases
        // may contain values in it, and this stabilization must not discard data.
        builder.Property<Guid?>("ProductBrandId1");

        builder.HasOne(x => x.Brand)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.ProductBrandId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Type)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.ProductTypeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => x.Price);
    }
}
