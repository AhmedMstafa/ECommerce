namespace ECommerce.UseCases.Common.Pagination;

/// <summary>
/// Small immutable value object for page results. Keeping this in UseCases
/// avoids leaking page math into controllers or persisting it in the database.
/// </summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int PageIndex,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 || TotalCount <= 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => PageIndex > 1;

    public bool HasNext => PageIndex < TotalPages;
}
