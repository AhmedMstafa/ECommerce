using ECommerce.Domain.Entities;
using ECommerce.Domain.Specifications;

namespace ECommerce.UseCases.Products.Specifications;

public class ProductsCountSpecification : BaseSpecification<Product>
{
    public ProductsCountSpecification(ProductSpecificationParameters parameters)
        : base(p =>
            (!parameters.BrandId.HasValue || p.ProductBrandId == parameters.BrandId.Value) &&
            (!parameters.TypeId.HasValue || p.ProductTypeId == parameters.TypeId.Value) &&
            (string.IsNullOrWhiteSpace(parameters.Search) || p.Name.ToLower().Contains(parameters.Search)))
    {
    }
}
