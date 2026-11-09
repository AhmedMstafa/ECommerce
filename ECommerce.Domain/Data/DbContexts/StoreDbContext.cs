using System.Linq.Expressions;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Entityies;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Domain.Data.DbContexts
{
    public class StoreDbContext(DbContextOptions<StoreDbContext> options) : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();

        public DbSet<ProductBrand> Brands => Set<ProductBrand>();


        public DbSet<ProductType> Types => Set<ProductType>();


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreDbContext).Assembly);

            ApplySoftDeleteQueryFilters(modelBuilder);

            base.OnModelCreating(modelBuilder);
        }

        private static void ApplySoftDeleteQueryFilters(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (entityType.BaseType is not null)
                    continue;

                if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                    continue;

                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var body = Expression.Equal(
                    Expression.Property(parameter, nameof(BaseEntity.IsDeleted)),
                    Expression.Constant(false));

                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(Expression.Lambda(body, parameter));
            }
        }

    }

}
