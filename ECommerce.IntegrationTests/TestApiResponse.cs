namespace ECommerce.IntegrationTests;

/// <summary>
/// Minimal client-side shape of ApiResponse&lt;T&gt;. The API's envelope is
/// public contract, so integration tests deserialize it instead of casting
/// internal controller results.
/// </summary>
public sealed record TestApiResponse<T>(
    int StatusCode,
    bool Success,
    T? Data,
    string? Message);
