using ECommerce.Domain.Entities;
using ECommerce.Domain.Specifications;
using ECommerce.Domain.Specifications.Builders;

namespace ECommerce.UseCases.Products.Specifications;

public class ProductsWithBrandAndTypeSpecification : BaseSpecification<Product>
{
    public ProductsWithBrandAndTypeSpecification(ProductSpecificationParameters parameters)
        : base(ProductSpecificationCriteria.For(parameters))
    {
        Query.Include(p => p.Brand);
        Query.Include(p => p.Type);

        // A deterministic tie-breaker is required for stable pagination. Without
        // it, SQL Server may return a different order for equal prices/names on
        // consecutive requests and clients can see duplicates or missing rows.
        switch (parameters.Sort?.ToLowerInvariant())
        {
            case ProductSortOptions.PriceAscending:
                Query.OrderBy(p => p.Price).ThenBy(p => p.Name).ThenBy(p => p.Id);
                break;
            case ProductSortOptions.PriceDescending:
                Query.OrderByDescending(p => p.Price).ThenBy(p => p.Name).ThenBy(p => p.Id);
                break;
            case ProductSortOptions.NameDescending:
                Query.OrderByDescending(p => p.Name).ThenBy(p => p.Id);
                break;
            default:
                Query.OrderBy(p => p.Name).ThenBy(p => p.Id);
                break;
        }

        Query.Paginate(parameters.PageIndex, parameters.PageSize);
    }

    public ProductsWithBrandAndTypeSpecification()
    {
        Query.Include(p => p.Brand);
        Query.Include(p => p.Type);
        Query.OrderBy(p => p.Name).ThenBy(p => p.Id);
    }
}
