using ECommerce.Domain.Entities;
using ECommerce.Domain.Specifications;
using ECommerce.Domain.Specifications.Builders;

namespace ECommerce.UseCases.Brands.Specifications;

public class GetAllBrandsSpecification : BaseSpecification<ProductBrand>
{
    public GetAllBrandsSpecification()
    {
        Query.OrderBy(b => b.Name)
             .AsNoTracking();
    }
}
