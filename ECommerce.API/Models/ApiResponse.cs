namespace ECommerce.API.Models;

public class ApiResponse<T>
{
    public int StatusCode { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
    public ApiMeta? Meta { get; set; }

    public static ApiResponse<T> SuccessResponse(
        T data,
        int statusCode = 200,
        string? message = null,
        ApiMeta? meta = null)
        => new() { Success = true, StatusCode = statusCode, Data = data, Message = message, Meta = meta };

    public static ApiResponse<T> FailureResponse(
        string message,
        int statusCode = 400,
        ApiMeta? meta = null)
        => new() { Success = false, StatusCode = statusCode, Message = message, Data = default, Meta = meta };
}
