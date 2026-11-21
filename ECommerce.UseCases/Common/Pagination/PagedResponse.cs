namespace ECommerce.UseCases.Common.Pagination;

/// <summary>
/// Reusable immutable page result. Keeping this in UseCases avoids leaking page
/// math into controllers or persisting it in the database. The API layer maps
/// these values into its response metadata without changing this shape.
/// </summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 || TotalCount <= 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;
}
