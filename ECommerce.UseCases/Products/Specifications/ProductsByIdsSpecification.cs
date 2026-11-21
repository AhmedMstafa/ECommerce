using ECommerce.Domain.Entities;
using ECommerce.Domain.Specifications;

namespace ECommerce.UseCases.Products.Specifications;

/// <summary>
/// Loads current SQL product data for a set of basket items in one query.
/// Soft-deleted products are excluded by the DbContext query filter.
/// </summary>
public sealed class ProductsByIdsSpecification : BaseSpecification<Product>
{
    public ProductsByIdsSpecification(IEnumerable<Guid> productIds)
        : base(product => productIds.Contains(product.Id))
    {
    }
}
