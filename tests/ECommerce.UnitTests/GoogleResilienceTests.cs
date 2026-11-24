using System.Net;
using ECommerce.Infrastructure.Auth;
using FluentAssertions;
using Google.Apis.Http;

namespace ECommerce.UnitTests;

public class GoogleResilienceTests
{
    [Fact]
    public async Task GoogleResilientHttpClientFactory_ShouldForwardThroughNamedHttpClient()
    {
        var recorder = new RecordingHandler();
        using var httpClient = new HttpClient(recorder);
        var factory = new GoogleResilientHttpClientFactory(new StubHttpClientFactory(httpClient));

        using var googleClient = factory.CreateHttpClient(new CreateHttpClientArgs());
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://oauth2.googleapis.com/token");

        using var response = await googleClient.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        recorder.Request.Should().NotBeNull();
        recorder.Request!.RequestUri!.ToString().Should().Be("https://oauth2.googleapis.com/token");
    }

    private sealed class StubHttpClientFactory(HttpClient client) : System.Net.Http.IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
