using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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

        public IAuditedRepository<T> AuditedRepository<T>() where T : AuditedEntity
        {
            var type = typeof(T);

            if (_repos.TryGetValue(type, out var repo))
                return (IAuditedRepository<T>)repo;

            var newRepo = new AuditedRepository<T>(dbContext);
            _repos.TryAdd(type, newRepo);

            return newRepo;
        }

        public IAuditedReadRepository<T> AuditedReadRepository<T>() where T : AuditedEntity
        {
            return AuditedRepository<T>();
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            var strategy = dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<T>(async () =>
            {
                await using var tx = await dbContext.Database.BeginTransactionAsync(ct);
                var result = await operation(ct);
                await tx.CommitAsync(ct);
                return result;
            });
        }

        public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
        {
            var strategy = dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await dbContext.Database.BeginTransactionAsync(ct);
                await operation(ct);
                await tx.CommitAsync(ct);
            });
        }

        public async Task<Result<T>> ExecuteInTransactionResultAsync<T>(Func<CancellationToken, Task<Result<T>>> operation, CancellationToken ct = default)
        {
            var strategy = dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<Result<T>>(async () =>
            {
                await using var tx = await dbContext.Database.BeginTransactionAsync(ct);
                var result = await operation(ct);
                if (result.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return result;
                }

                await tx.CommitAsync(ct);
                return result;
            });
        }
    }
}
