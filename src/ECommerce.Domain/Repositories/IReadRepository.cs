using ECommerce.Domain.Entities;
using ECommerce.Domain.Specifications;

namespace ECommerce.Domain.Repositories
{
    public interface IReadRepository<T> where T : BaseEntity
    {
        Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);

        Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);

        Task<T?> GetEntityWithSpecAsync(ISpecification<T> spec, CancellationToken ct = default);

        Task<IReadOnlyList<T>> GetAllWithSpecAsync(ISpecification<T> spec, CancellationToken ct = default);

        Task<TResult?> GetEntityWithSpecAsync<TResult>(ISpecification<T, TResult> spec, CancellationToken ct = default);

        Task<IReadOnlyList<TResult>> GetAllWithSpecAsync<TResult>(ISpecification<T, TResult> spec, CancellationToken ct = default);

        Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct = default);

        Task<int> CountAsync(CancellationToken ct = default);
    }
}
