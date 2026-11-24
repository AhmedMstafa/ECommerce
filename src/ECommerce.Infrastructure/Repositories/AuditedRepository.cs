using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.Domain.Specifications;
using ECommerce.Infrastructure.Persistence;
using ECommerce.Infrastructure.Specifications;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Infrastructure.Repositories;

public class AuditedRepository<T>(StoreDbContext dbContext) : IAuditedRepository<T>, IAuditedReadRepository<T> where T : AuditedEntity
{
    protected readonly StoreDbContext _dbContext = dbContext;

    public void Add(T entity) => _dbContext.Set<T>().Add(entity);

    public void Delete(T entity) => _dbContext.Set<T>().Remove(entity);

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) =>
        await _dbContext.Set<T>().AsNoTracking().ToListAsync(ct);

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _dbContext.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<T>> GetAllWithSpecAsync(ISpecification<T> spec, CancellationToken ct = default) =>
        await ApplySpecification(spec).ToListAsync(ct);

    public async Task<T?> GetEntityWithSpecAsync(ISpecification<T> spec, CancellationToken ct = default) =>
        await ApplySpecification(spec).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TResult>> GetAllWithSpecAsync<TResult>(ISpecification<T, TResult> spec, CancellationToken ct = default) =>
        await ApplySpecification(spec).ToListAsync(ct);

    public async Task<TResult?> GetEntityWithSpecAsync<TResult>(ISpecification<T, TResult> spec, CancellationToken ct = default) =>
        await ApplySpecification(spec).FirstOrDefaultAsync(ct);

    public async Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct = default) =>
        await ApplySpecification(spec).CountAsync(ct);

    public async Task<int> CountAsync(CancellationToken ct = default) =>
        await _dbContext.Set<T>().CountAsync(ct);

    public void Update(T entity) => _dbContext.Set<T>().Update(entity);

    private IQueryable<T> ApplySpecification(ISpecification<T> spec) =>
        SpecificationEvaluator.GetQuery(_dbContext.Set<T>().AsQueryable(), spec);

    private IQueryable<TResult> ApplySpecification<TResult>(ISpecification<T, TResult> spec) =>
        SpecificationEvaluator.GetQuery(_dbContext.Set<T>().AsQueryable(), spec);
}
