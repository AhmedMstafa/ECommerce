namespace ECommerce.UseCases.Products.Specifications;

public class ProductSpecificationParameters
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 50;
    public const int MaxSearchLength = 100;

    public int PageIndex { get; set; } = 1;

    /// <summary>
    /// Kept as a plain value so FluentValidation can reject invalid input.
    /// Silent clamping hid client bugs and made the response metadata misleading.
    /// </summary>
    public int PageSize { get; set; } = DefaultPageSize;

    public Guid? BrandId { get; set; }

    public Guid? TypeId { get; set; }

    public string? Sort { get; set; }

    private string? _search;
    public string? Search
    {
        get => _search;
        set => _search = string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();
    }
}
