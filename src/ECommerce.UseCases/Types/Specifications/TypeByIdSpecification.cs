using ECommerce.Domain.Entities;
using ECommerce.Domain.Specifications;
using ECommerce.Domain.Specifications.Builders;

namespace ECommerce.UseCases.Types.Specifications;

public class TypeByIdSpecification : BaseSpecification<ProductType>
{
    public TypeByIdSpecification(Guid id) : base(t => t.Id == id)
    {
        Query.AsNoTracking();
    }
}
