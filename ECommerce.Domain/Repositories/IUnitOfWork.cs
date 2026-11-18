using ECommerce.Domain.Entityies;

namespace ECommerce.Domain.Repositories
{
    public interface IUnitOfWork
    {
        IRepository<T> Repository<T>() where T : BaseEntity;

        IReadRepository<T> ReadRepository<T>() where T : BaseEntity;

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
