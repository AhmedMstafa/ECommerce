using ECommerce.Domain.Entities;
using ECommerce.Domain.Specifications;

namespace ECommerce.UseCases.Products.Specifications;

public class ProductsCountSpecification : BaseSpecification<Product>
{
    public ProductsCountSpecification(ProductSpecificationParameters parameters)
        : base(ProductSpecificationCriteria.For(parameters))
    {
    }
}
