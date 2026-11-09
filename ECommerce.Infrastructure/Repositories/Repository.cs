using ECommerce.Domain.Data.DbContexts;
using ECommerce.Domain.Entityies;
using ECommerce.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Infrastructure.Repositories
{
    public class Repository<T>(StoreDbContext dbContext) : IRepository<T> where T : BaseEntity
    {
        protected readonly StoreDbContext _dbContext = dbContext;

        public void Add(T entity)
        {
            _dbContext.Set<T>().Add(entity);
        }

        public void Delete(T entity)
        {
            entity.MarkAsDeleted();
            _dbContext.Set<T>().Update(entity); 
        }

        public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        {
            return await _dbContext.Set<T>()
                .AsNoTracking()
                .ToListAsync(ct);
        }

        public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return await _dbContext.Set<T>() 
                .FirstOrDefaultAsync(e => e.Id == id, ct);
        }

        public void Update(T entity)
        {
            _dbContext.Set<T>().Update(entity);
        }
    }
}