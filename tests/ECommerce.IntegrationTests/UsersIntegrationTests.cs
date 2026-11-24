using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ECommerce.UseCases.Auth.Commands.Register;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Users.Dtos;
using FluentAssertions;

namespace ECommerce.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class UsersIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task Me_ShouldReturnCurrentUser()
    {
        var reg = await RegisterAsync();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reg.Token);

        var res = await fixture.Client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<TestApiResponse<UserProfileResponse>>();
        body!.Data!.Email.Should().Be(reg.Email);
    }

    [Fact]
    public async Task Me_ShouldRejectUnauthorized()
    {
        var res = await fixture.Client.GetAsync("/api/v1/users/me");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateProfile_DescriptionSemantics_ShouldWork()
    {
        var reg = await RegisterAsync();

        // set value
        var setBody = await PutProfileAsync(reg.Token, new Dictionary<string, object?>
        {
            ["firstName"] = "Jane",
            ["lastName"] = "Doe",
            ["description"] = "Hello",
            ["pictureUrl"] = null
        });
        setBody.Description.Should().Be("Hello");

        // omitted description preserves
        var omitBody = await PutProfileAsync(reg.Token, new Dictionary<string, object?>
        {
            ["firstName"] = "Jane",
            ["lastName"] = "Doe",
            ["pictureUrl"] = null
        });
        omitBody.Description.Should().Be("Hello");

        // explicit null clears
        var clearBody = await PutProfileAsync(reg.Token, new Dictionary<string, object?>
        {
            ["firstName"] = "Jane",
            ["lastName"] = "Doe",
            ["description"] = null,
            ["pictureUrl"] = null
        });
        clearBody.Description.Should().BeNull();
    }

    [Fact]
    public async Task Addresses_ShouldSupportAddRemoveDefaultAndOwnership()
    {
        var userA = await RegisterAsync();
        var userB = await RegisterAsync();

        var addr1 = await AddAddressAsync(userA.Token, new { firstName="A", lastName="One", street="S1", city="C", state="ST", zipCode="12345", country="US", isDefault=false });
        addr1.IsDefault.Should().BeTrue(); // first address becomes default

        var addr2 = await AddAddressAsync(userA.Token, new { firstName="A", lastName="Two", street="S2", city="C", state="ST", zipCode="12345", country="US", isDefault=true });
        addr2.IsDefault.Should().BeTrue();

        var defaultAfterSecond = await GetDefaultAddressAsync(userA.Token);
        defaultAfterSecond.Id.Should().Be(addr2.Id);

        // user B cannot remove user A address
        using var delOther = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/users/addresses/{addr2.Id}");
        delOther.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userB.Token);
        var delOtherRes = await fixture.Client.SendAsync(delOther);
        delOtherRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // remove default address, remaining should become default
        using var del = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/users/addresses/{addr2.Id}");
        del.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userA.Token);
        var delRes = await fixture.Client.SendAsync(del);
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var defaultAfterDelete = await GetDefaultAddressAsync(userA.Token);
        defaultAfterDelete.Id.Should().Be(addr1.Id);

        // remove last address -> no default
        using var del2 = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/users/addresses/{addr1.Id}");
        del2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userA.Token);
        (await fixture.Client.SendAsync(del2)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var getDefault = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/addresses/default");
        getDefault.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userA.Token);
        var missing = await fixture.Client.SendAsync(getDefault);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<AuthResponse> RegisterAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        const string password = "StrongPass1!";
        var res = await fixture.Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterCommand(email, password, "Test", "User"));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<TestApiResponse<AuthResponse>>();
        return body!.Data!;
    }

    private async Task<UserProfileResponse> PutProfileAsync(string token, Dictionary<string, object?> payload)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/users/profile")
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await fixture.Client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<TestApiResponse<UserProfileResponse>>();
        return body!.Data!;
    }

    private async Task<AddressResponse> AddAddressAsync(string token, object payload)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/addresses")
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await fixture.Client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<TestApiResponse<AddressResponse>>();
        return body!.Data!;
    }

    private async Task<AddressResponse> GetDefaultAddressAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/addresses/default");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await fixture.Client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<TestApiResponse<AddressResponse>>();
        return body!.Data!;
    }
}
