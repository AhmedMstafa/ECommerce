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

        var (field, direction) = ProductSortOptions.Resolve(parameters);
        var descending = string.Equals(direction, ProductSortOptions.Descending, StringComparison.OrdinalIgnoreCase);

        // A deterministic tie-breaker is required for stable pagination. Without
        // it, SQL Server may return a different order for equal prices/names on
        // consecutive requests and clients can see duplicates or missing rows.
        switch (field.ToLowerInvariant())
        {
            case ProductSortOptions.Price:
                if (descending)
                {
                    Query.OrderByDescending(p => p.Price).ThenByDescending(p => p.Name).ThenBy(p => p.Id);
                }
                else
                {
                    Query.OrderBy(p => p.Price).ThenBy(p => p.Name).ThenBy(p => p.Id);
                }
                break;

            case ProductSortOptions.CreatedAt:
                if (descending)
                {
                    Query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id);
                }
                else
                {
                    Query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id);
                }
                break;

            default:
                if (descending)
                {
                    Query.OrderByDescending(p => p.Name).ThenByDescending(p => p.Id);
                }
                else
                {
                    Query.OrderBy(p => p.Name).ThenBy(p => p.Id);
                }
                break;
        }

        // The evaluator applies ordering before Skip/Take, so paging is stable.
        Query.Paginate(parameters.PageNumber, parameters.PageSize);
    }

    public ProductsWithBrandAndTypeSpecification()
    {
        Query.Include(p => p.Brand);
        Query.Include(p => p.Type);
        Query.OrderBy(p => p.Name).ThenBy(p => p.Id);
    }
}
