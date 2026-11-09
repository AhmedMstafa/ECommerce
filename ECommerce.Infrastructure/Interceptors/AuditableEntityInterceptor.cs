using ECommerce.Domain.Entityies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ECommerce.Infrastructure.Interceptors
{
    /// <summary>
    /// Fills audit columns and turns physical deletes into soft deletes
    /// for every entity that inherits <see cref="BaseEntity"/>.
    /// </summary>
    public sealed class AuditableEntityInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            ApplyAuditRules(eventData.Context);

            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ApplyAuditRules(eventData.Context);

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private static void ApplyAuditRules(DbContext? context)
        {
            if (context is null)
                return;

            var utcNow = DateTime.UtcNow;
            var utcNowOffset = DateTimeOffset.UtcNow;

            foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Property(nameof(BaseEntity.CreatedAt)).CurrentValue = utcNow;
                        entry.Property(nameof(BaseEntity.UpdatedAt)).CurrentValue = utcNowOffset;
                        break;

                    case EntityState.Modified:
                        entry.Property(nameof(BaseEntity.UpdatedAt)).CurrentValue = utcNowOffset;
                        SetDeletedAtWhenSoftDeleted(entry, utcNowOffset);
                        break;

                    case EntityState.Deleted:
                        // Soft delete: keep the row, flip the flag instead.
                        entry.State = EntityState.Modified;
                        entry.Property(nameof(BaseEntity.IsDeleted)).CurrentValue = true;
                        entry.Property(nameof(BaseEntity.DeletedAt)).CurrentValue = utcNowOffset;
                        entry.Property(nameof(BaseEntity.UpdatedAt)).CurrentValue = utcNowOffset;
                        break;
                }
            }
        }

        private static void SetDeletedAtWhenSoftDeleted(
            EntityEntry<BaseEntity> entry,
            DateTimeOffset utcNow)
        {
            var isDeleted = entry.Property(nameof(BaseEntity.IsDeleted));

            if (isDeleted.CurrentValue is not true)
                return;

            var deletedAt = entry.Property(nameof(BaseEntity.DeletedAt));

            if (deletedAt.CurrentValue is null)
                deletedAt.CurrentValue = utcNow;
        }
    }
}
