using System.Globalization;
using ECommerce.UseCases.Products.Specifications;

namespace ECommerce.UseCases.Products.Caching;

/// <summary>
/// Logical cache keys for product reads. The Redis instance name (ECommerce:)
/// is applied by IDistributedCache, and the generation suffix is applied by
/// the cache service, so these keys stay stable and testable.
/// Every parameter that can change the result is part of the key.
/// </summary>
public static class ProductCacheKeys
{
    public static string List(ProductSpecificationParameters parameters)
    {
        var (field, direction) = ProductSortOptions.Resolve(parameters);

        return string.Concat(
            "Products:Page:", Token(parameters.PageNumber.ToString(CultureInfo.InvariantCulture)),
            ":Size:", Token(parameters.PageSize.ToString(CultureInfo.InvariantCulture)),
            ":Search:", Token(parameters.Search),
            ":Brand:", Token(parameters.BrandId?.ToString("N")),
            ":Type:", Token(parameters.TypeId?.ToString("N")),
            ":MinPrice:", Token(parameters.MinPrice?.ToString(CultureInfo.InvariantCulture)),
            ":MaxPrice:", Token(parameters.MaxPrice?.ToString(CultureInfo.InvariantCulture)),
            ":Sort:", Token(field),
            ":Order:", Token(direction),
            ":LegacySort:", Token(parameters.Sort),
            ":SortBy:", Token(parameters.SortBy),
            ":SortOrder:", Token(parameters.SortOrder));
    }

    public static string Details(Guid id) => $"Products:Id:{id:N}";

    private static string Token(string? value) =>
        Uri.EscapeDataString(string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().ToLowerInvariant());
}
