using ECommerce.Domain.Entities;
using ECommerce.Domain.Specifications;
using ECommerce.Domain.Specifications.Builders;

namespace ECommerce.UseCases.Products.Specifications;

public class ProductByIdWithBrandAndTypeSpecification : BaseSpecification<Product>
{
    public ProductByIdWithBrandAndTypeSpecification(Guid id)
        : base(p => p.Id == id)
    {
        Query.Include(p => p.Brand);
        Query.Include(p => p.Type);
    }
}
