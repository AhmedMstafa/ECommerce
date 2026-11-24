using System.Linq.Expressions;
using ECommerce.Domain.Entities;

namespace ECommerce.UseCases.Products.Specifications;

/// <summary>
/// Shared filter so the paged query and its count query cannot drift apart.
/// The expression is translated by EF Core, so filtering stays in the database.
/// </summary>
internal static class ProductSpecificationCriteria
{
    public static Expression<Func<Product, bool>> For(ProductSpecificationParameters parameters) => p =>
        (!parameters.BrandId.HasValue || p.ProductBrandId == parameters.BrandId.Value) &&
        (!parameters.TypeId.HasValue || p.ProductTypeId == parameters.TypeId.Value) &&
        (!parameters.MinPrice.HasValue || p.Price >= parameters.MinPrice.Value) &&
        (!parameters.MaxPrice.HasValue || p.Price <= parameters.MaxPrice.Value) &&
        (string.IsNullOrWhiteSpace(parameters.Search) ||
         p.Name.ToLower().Contains(parameters.Search!) ||
         (p.Description != null && p.Description.ToLower().Contains(parameters.Search!)));
}
