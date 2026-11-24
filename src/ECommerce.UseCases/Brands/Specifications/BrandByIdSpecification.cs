using ECommerce.Domain.Entities;
using ECommerce.Domain.Specifications;
using ECommerce.Domain.Specifications.Builders;

namespace ECommerce.UseCases.Brands.Specifications;

public class BrandByIdSpecification : BaseSpecification<ProductBrand>
{
    public BrandByIdSpecification(Guid id) : base(b => b.Id == id)
    {
        Query.AsNoTracking();
    }
}
