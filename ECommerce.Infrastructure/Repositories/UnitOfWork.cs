using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.Infrastructure.Persistence;
using System.Collections.Concurrent;

namespace ECommerce.Infrastructure.Repositories
{
    public class UnitOfWork(StoreDbContext dbContext) : IUnitOfWork
    {
        private readonly ConcurrentDictionary<Type, object> _repos = new();

        public IRepository<T> Repository<T>() where T : BaseEntity
        {
            var type = typeof(T);

            if (_repos.TryGetValue(type, out var repo)) 
                return (IRepository<T>)repo;

            var newRepo = new Repository<T>(dbContext);
            _repos.TryAdd(type, newRepo);

            return newRepo;
        }

        public IReadRepository<T> ReadRepository<T>() where T : BaseEntity
        {
            return Repository<T>();
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
