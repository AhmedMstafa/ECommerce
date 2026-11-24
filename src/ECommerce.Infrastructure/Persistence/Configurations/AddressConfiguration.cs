using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Infrastructure.Persistence.Configurations;

public class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.Property(x => x.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(x => x.LastName).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Street).IsRequired().HasMaxLength(256);
        builder.Property(x => x.City).IsRequired().HasMaxLength(100);
        builder.Property(x => x.State).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ZipCode).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Country).IsRequired().HasMaxLength(100);

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.UserId, x.IsDefault })
            .IsUnique()
            .HasFilter("[IsDefault] = 1 AND [IsDeleted] = 0");

        builder.HasOne<AppUser>()
            .WithMany(u => u.Addresses)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
