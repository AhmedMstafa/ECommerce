namespace ECommerce.UseCases.Products.Specifications;

public static class ProductSortOptions
{
    public const string NameAscending = "nameasc";
    public const string NameDescending = "namedesc";
    public const string PriceAscending = "priceasc";
    public const string PriceDescending = "pricedesc";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        NameAscending,
        NameDescending,
        PriceAscending,
        PriceDescending
    };
}
