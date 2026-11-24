namespace ECommerce.UseCases.Products.Specifications;

/// <summary>
/// Whitelist of database columns that may be used for product sorting.
/// Arbitrary property names are never translated into order expressions.
/// </summary>
public static class ProductSortOptions
{
    public const string Name = "name";
    public const string Price = "price";
    public const string CreatedAt = "createdat";

    public const string Ascending = "asc";
    public const string Descending = "desc";

    // Legacy combined values are still accepted for existing clients.
    public const string NameAscending = "nameasc";
    public const string NameDescending = "namedesc";
    public const string PriceAscending = "priceasc";
    public const string PriceDescending = "pricedesc";
    public const string CreatedAtAscending = "createdatasc";
    public const string CreatedAtDescending = "createdatdesc";

    public static readonly IReadOnlySet<string> AllowedFields =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Name, Price, CreatedAt };

    public static readonly IReadOnlySet<string> AllowedDirections =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Ascending, Descending };

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Name, Price, CreatedAt,
            NameAscending, NameDescending,
            PriceAscending, PriceDescending,
            CreatedAtAscending, CreatedAtDescending
        };

    /// <summary>
    /// Resolves the new sortBy/sortOrder pair, falling back to the legacy sort
    /// value and finally to a deterministic default.
    /// </summary>
    public static (string Field, string Direction) Resolve(ProductSpecificationParameters parameters)
    {
        if (!string.IsNullOrWhiteSpace(parameters.SortBy))
        {
            var direction = string.IsNullOrWhiteSpace(parameters.SortOrder)
                ? Ascending
                : parameters.SortOrder!;

            return (parameters.SortBy!, direction);
        }

        if (!string.IsNullOrWhiteSpace(parameters.Sort))
        {
            foreach (var field in new[] { Name, Price, CreatedAt })
            {
                foreach (var direction in new[] { Ascending, Descending })
                {
                    if (string.Equals(parameters.Sort, field + direction, StringComparison.OrdinalIgnoreCase))
                    {
                        return (field, direction);
                    }
                }
            }

            foreach (var field in new[] { Name, Price, CreatedAt })
            {
                if (string.Equals(parameters.Sort, field, StringComparison.OrdinalIgnoreCase))
                {
                    var direction = string.IsNullOrWhiteSpace(parameters.SortOrder)
                        ? Ascending
                        : parameters.SortOrder!;

                    return (field, direction);
                }
            }
        }

        return (Name, Ascending);
    }
}
