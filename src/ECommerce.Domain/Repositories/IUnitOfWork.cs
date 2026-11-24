using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;

namespace ECommerce.Domain.Repositories
{
    public interface IUnitOfWork
    {
        IRepository<T> Repository<T>() where T : BaseEntity;

        IReadRepository<T> ReadRepository<T>() where T : BaseEntity;

        IAuditedRepository<T> AuditedRepository<T>() where T : AuditedEntity;

        IAuditedReadRepository<T> AuditedReadRepository<T>() where T : AuditedEntity;

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

        Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);

        Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default);

        Task<Result<T>> ExecuteInTransactionResultAsync<T>(Func<CancellationToken, Task<Result<T>>> operation, CancellationToken ct = default);
    }
}
