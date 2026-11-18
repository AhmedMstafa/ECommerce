using ECommerce.Domain.Entities;
using ECommerce.Domain.Specifications;
using ECommerce.Domain.Specifications.Builders;

namespace ECommerce.UseCases.Products.Specifications;

public class ProductsWithBrandAndTypeSpecification : BaseSpecification<Product>
{
    public ProductsWithBrandAndTypeSpecification(ProductSpecificationParameters parameters)
        : base(p =>
            (!parameters.BrandId.HasValue || p.ProductBrandId == parameters.BrandId.Value) &&
            (!parameters.TypeId.HasValue || p.ProductTypeId == parameters.TypeId.Value) &&
            (string.IsNullOrWhiteSpace(parameters.Search) || p.Name.ToLower().Contains(parameters.Search)))
    {
        Query.Include(p => p.Brand);
        Query.Include(p => p.Type);

        if (!string.IsNullOrWhiteSpace(parameters.Sort))
        {
            switch (parameters.Sort.ToLower())
            {
                case "priceasc":
                    Query.OrderBy(p => p.Price);
                    break;
                case "pricedesc":
                    Query.OrderByDescending(p => p.Price);
                    break;
                default:
                    Query.OrderBy(p => p.Name);
                    break;
            }
        }
        else
        {
            Query.OrderBy(p => p.Name);
        }

        Query.Paginate(parameters.PageIndex, parameters.PageSize);
    }

    public ProductsWithBrandAndTypeSpecification()
    {
        Query.Include(p => p.Brand);
        Query.Include(p => p.Type);
        Query.OrderBy(p => p.Name);
    }
}
