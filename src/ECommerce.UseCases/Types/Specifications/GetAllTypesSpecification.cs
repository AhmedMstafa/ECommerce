using ECommerce.Domain.Entities;
using ECommerce.Domain.Specifications;
using ECommerce.Domain.Specifications.Builders;

namespace ECommerce.UseCases.Types.Specifications;

public class GetAllTypesSpecification : BaseSpecification<ProductType>
{
    public GetAllTypesSpecification()
    {
        Query.OrderBy(t => t.Name)
             .AsNoTracking();
    }
}
