using ECommerce.Domain.Entities;

namespace ECommerce.Domain.Repositories;

public interface IAuditedRepository<T> : IAuditedReadRepository<T> where T : AuditedEntity
{
    void Add(T entity);
    void Update(T entity);
    void Delete(T entity);
}
