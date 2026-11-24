using System.Text.Json.Serialization;
using ECommerce.UseCases.Common.Pagination;

namespace ECommerce.API.Models;

/// <summary>
/// Metadata attached to API responses. The trace id correlates a response with
/// the Serilog CorrelationId already emitted for the request. Pagination fields
/// are omitted unless the endpoint returns a page.
/// </summary>
public sealed class ApiMeta
{
    public string TraceId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PageNumber { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PageSize { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalPages { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasPreviousPage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasNextPage { get; set; }

    public static ApiMeta For(string traceId) => new() { TraceId = traceId };

    public static ApiMeta ForPage<T>(string traceId, PagedResponse<T> page) => new()
    {
        TraceId = traceId,
        PageNumber = page.PageNumber,
        PageSize = page.PageSize,
        TotalCount = page.TotalCount,
        TotalPages = page.TotalPages,
        HasPreviousPage = page.HasPreviousPage,
        HasNextPage = page.HasNextPage
    };
}
