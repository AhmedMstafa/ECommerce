namespace ECommerce.UseCases.Products.Specifications;

public class ProductSpecificationParameters
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 50;
    public const int MaxSearchLength = 100;

    /// <summary>Canonical page number. Query string: pageNumber.</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>
    /// Backward-compatible alias for existing clients that send pageIndex.
    /// </summary>
    public int PageIndex
    {
        get => PageNumber;
        set => PageNumber = value;
    }

    /// <summary>
    /// Kept as a plain value so FluentValidation can reject invalid input.
    /// Silent clamping hid client bugs and made the response metadata misleading.
    /// </summary>
    public int PageSize { get; set; } = DefaultPageSize;

    public Guid? BrandId { get; set; }

    public Guid? TypeId { get; set; }

    public decimal? MinPrice { get; set; }

    public decimal? MaxPrice { get; set; }

    /// <summary>Sortable field from the ProductSortOptions whitelist.</summary>
    public string? SortBy
    {
        get => _sortBy;
        set => _sortBy = Normalize(value);
    }

    /// <summary>asc or desc.</summary>
    public string? SortOrder
    {
        get => _sortOrder;
        set => _sortOrder = Normalize(value);
    }

    /// <summary>
    /// Legacy combined sort value such as priceAsc. SortBy/SortOrder take precedence.
    /// </summary>
    public string? Sort
    {
        get => _sort;
        set => _sort = Normalize(value);
    }

    private string? _search;
    public string? Search
    {
        get => _search;
        set => _search = Normalize(value);
    }

    private string? _sortBy;
    private string? _sortOrder;
    private string? _sort;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim().ToLowerInvariant();
}
