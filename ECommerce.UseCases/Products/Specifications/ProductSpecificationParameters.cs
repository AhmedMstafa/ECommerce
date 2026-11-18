namespace ECommerce.UseCases.Products.Specifications;

public class ProductSpecificationParameters
{
    private const int MaxPageSize = 50;

    public int PageIndex { get; set; } = 1;

    private int _pageSize = 10;
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = (value > MaxPageSize) ? MaxPageSize : (value <= 0 ? 10 : value);
    }

    public Guid? BrandId { get; set; }

    public Guid? TypeId { get; set; }

    public string? Sort { get; set; }

    private string? _search;
    public string? Search
    {
        get => _search;
        set => _search = value?.Trim().ToLower();
    }
}
