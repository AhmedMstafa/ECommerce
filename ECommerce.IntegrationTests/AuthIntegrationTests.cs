using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Persistence;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Common.Email;
using ECommerce.UseCases.Auth.Commands.Login;
using ECommerce.UseCases.Auth.Commands.Register;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class AuthIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task RegisterAndLogin_ShouldReturnJwt_WithValidCredentials()
    {
        var email = $"register-{Guid.NewGuid():N}@example.com";
        const string password = "StrongPass1!";

        var registerResponse = await fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterCommand(email, password, "Integration", "User"));

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var registerBody = await registerResponse.Content
            .ReadFromJsonAsync<TestApiResponse<AuthResponse>>();
        registerBody!.Data!.Token.Should().NotBeNullOrWhiteSpace();
        registerBody.Data.Email.Should().Be(email);

        var loginResponse = await fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginCommand(email, password));

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBody = await loginResponse.Content
            .ReadFromJsonAsync<TestApiResponse<AuthResponse>>();
        loginBody!.Data!.Token.Should().NotBeNullOrWhiteSpace();
        loginBody.Data.Email.Should().Be(email);
    }

    [Fact]
    public async Task Register_ShouldRejectWeakPassword()
    {
        var response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterCommand(
                $"weak-{Guid.NewGuid():N}@example.com",
                "alllowercase",
                "Weak",
                "Password"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ForgotPassword_ShouldQueueOutboxMessage_WithoutSendingDirectEmail()
    {
        var sendsBefore = fixture.EmailService.SendCount;

        var response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password",
            new { email = IntegrationTestFixture.AdminEmail });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.EmailService.SendCount.Should().Be(sendsBefore);

        var messages = await ReadOutboxMessagesAsync();
        var message = messages.SingleOrDefault(item =>
            item.Type == "Email" &&
            item.Content.Contains(IntegrationTestFixture.AdminEmail, StringComparison.Ordinal));

        message.Should().NotBeNull();

        message!.Status.Should().Be(OutboxMessageStatus.Pending);
        message.ProcessedOnUtc.Should().BeNull();
        message.RetryCount.Should().Be(0);
        message.IdempotencyKey.Should().StartWith("sha256:");

        var payload = JsonSerializer.Deserialize<EmailOutboxPayload>(message.Content)!;
        payload.To.Should().Be(IntegrationTestFixture.AdminEmail);
        payload.IsHtml.Should().BeTrue();
        payload.Body.Should().Contain("https://client.example.com/reset-password?email=");
    }

    [Fact]
    public async Task ForgotPassword_ShouldNotRevealUnknownAccount_OrQueueEmail()
    {
        var countBefore = (await ReadOutboxMessagesAsync()).Count;
        var sendsBefore = fixture.EmailService.SendCount;

        var response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password",
            new { email = $"missing-{Guid.NewGuid():N}@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadOutboxMessagesAsync()).Count.Should().Be(countBefore);
        fixture.EmailService.SendCount.Should().Be(sendsBefore);
    }

    [Fact]
    public async Task GoogleLogin_ShouldRejectInvalidToken()
    {
        var response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/google-login",
            new { idToken = StubGoogleTokenValidator.InvalidToken });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GoogleLogin_ShouldCreateNewUser_ForVerifiedToken()
    {
        var response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/google-login",
            new { idToken = StubGoogleTokenValidator.NewUserToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<TestApiResponse<AuthResponse>>();
        body!.Data!.Email.Should().Be(StubGoogleTokenValidator.NewUserEmail);
        body.Data.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GoogleLogin_ShouldNotAutoLink_ExistingEmailOwner()
    {
        var response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/auth/google-login",
            new { idToken = StubGoogleTokenValidator.ExistingEmailToken });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task LinkGoogle_ShouldRejectMismatchedEmail()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/link-google")
        {
            Content = JsonContent.Create(new
            {
                idToken = StubGoogleTokenValidator.NewUserToken
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminToken);

        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<List<OutboxMessage>> ReadOutboxMessagesAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        return await dbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();
    }
}
