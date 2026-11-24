using Google.Apis.Http;

namespace ECommerce.Infrastructure.Auth;

/// <summary>
/// Bridges the Google SDK's <see cref="Google.Apis.Http.IHttpClientFactory"/>
/// to ASP.NET Core's named HttpClient. The SDK manages JWKS retrieval and
/// caching; the named client adds timeout/retry/circuit-breaker policies to
/// that one real HTTP dependency.
/// </summary>
public sealed class GoogleResilientHttpClientFactory(
    System.Net.Http.IHttpClientFactory httpClientFactory) : Google.Apis.Http.IHttpClientFactory
{
    public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var httpClient = httpClientFactory.CreateClient(GoogleTokenValidator.ResilientHttpClientName);
        var adapterHandler = new HttpClientAdapterHandler(httpClient);

        return new ConfigurableHttpClient(new ConfigurableMessageHandler(adapterHandler)
        {
            ApplicationName = args.ApplicationName,
            GoogleApiClientHeader = args.GoogleApiClientHeader,
            UniverseDomain = args.UniverseDomain
        });
    }
}

/// <summary>
/// Forwards a request to the ASP.NET Core-managed HttpClient. A copy is sent
/// because an HttpRequestMessage cannot be sent through two pipelines. The
/// injected client is owned by IHttpClientFactory and is not disposed here.
/// </summary>
internal sealed class HttpClientAdapterHandler(HttpClient httpClient) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var forwarded = await CloneAsync(request, cancellationToken);

        var response = await httpClient.SendAsync(
            forwarded,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.RequestMessage = request;
        return response;
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    protected override void Dispose(bool disposing)
    {
        // The HttpClient is pooled and owned by Microsoft IHttpClientFactory.
    }
}
