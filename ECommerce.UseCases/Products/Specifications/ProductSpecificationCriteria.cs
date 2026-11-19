using System.Linq.Expressions;
using ECommerce.Domain.Entities;

namespace ECommerce.UseCases.Products.Specifications;

/// <summary>
/// Shared filter so the paged query and its count query cannot drift apart.
/// </summary>
internal static class ProductSpecificationCriteria
{
    public static Expression<Func<Product, bool>> For(ProductSpecificationParameters parameters) => p =>
        (!parameters.BrandId.HasValue || p.ProductBrandId == parameters.BrandId.Value) &&
        (!parameters.TypeId.HasValue || p.ProductTypeId == parameters.TypeId.Value) &&
        (string.IsNullOrWhiteSpace(parameters.Search) || p.Name.ToLower().Contains(parameters.Search!));
}
