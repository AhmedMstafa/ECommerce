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

    [Fact]
    public async Task Register_ShouldReturnRefreshToken_AndQueueConfirmationEmail()
    {
        var email = $"confirm-{Guid.NewGuid():N}@example.com";
        const string password = "StrongPass1!";

        var response = await fixture.Client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterCommand(email, password, "Confirm", "User"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TestApiResponse<AuthResponse>>();
        body!.Data!.RefreshToken.Should().NotBeNullOrWhiteSpace();
        body.Data.RefreshTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);

        var messages = await ReadOutboxMessagesAsync();
        messages.Should().Contain(m => m.Type == "Email" && m.Content.Contains(email, StringComparison.Ordinal) && m.Content.Contains("confirm-email", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Login_ShouldSucceed_ForUnconfirmedUser()
    {
        var email = $"unconfirmed-{Guid.NewGuid():N}@example.com";
        const string password = "StrongPass1!";
        var register = await fixture.Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterCommand(email, password, "Unconfirmed", "User"));
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await fixture.Client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand(email, password));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<TestApiResponse<AuthResponse>>();
        body!.Data!.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Refresh_ShouldRotateToken_AndRejectReuse()
    {
        var email = $"refresh-{Guid.NewGuid():N}@example.com";
        const string password = "StrongPass1!";
        var register = await (await fixture.Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterCommand(email, password, "Refresh", "User")))
            .Content.ReadFromJsonAsync<TestApiResponse<AuthResponse>>();
        var oldRefresh = register!.Data!.RefreshToken;

        var refresh1 = await fixture.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = oldRefresh });
        refresh1.StatusCode.Should().Be(HttpStatusCode.OK);
        var refresh1Body = await refresh1.Content.ReadFromJsonAsync<TestApiResponse<AuthResponse>>();
        refresh1Body!.Data!.RefreshToken.Should().NotBeNullOrWhiteSpace();
        refresh1Body.Data.RefreshToken.Should().NotBe(oldRefresh);

        var reuse = await fixture.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = oldRefresh });
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ShouldRevokeRefreshToken_AndPreventUse()
    {
        var email = $"logout-{Guid.NewGuid():N}@example.com";
        const string password = "StrongPass1!";
        var regBody = await (await fixture.Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterCommand(email, password, "Logout", "User")))
            .Content.ReadFromJsonAsync<TestApiResponse<AuthResponse>>();
        var token = regBody!.Data!.Token;
        var refresh = regBody.Data.RefreshToken;

        using var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
        {
            Content = JsonContent.Create(new { refreshToken = refresh })
        };
        logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var logout = await fixture.Client.SendAsync(logoutReq);
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshAfter = await fixture.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh });
        refreshAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ShouldRejectAnotherUsersToken()
    {
        var email1 = $"logout-a-{Guid.NewGuid():N}@example.com";
        var email2 = $"logout-b-{Guid.NewGuid():N}@example.com";
        const string password = "StrongPass1!";
        var reg1 = await (await fixture.Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterCommand(email1, password, "A", "User")))
            .Content.ReadFromJsonAsync<TestApiResponse<AuthResponse>>();
        var reg2 = await (await fixture.Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterCommand(email2, password, "B", "User")))
            .Content.ReadFromJsonAsync<TestApiResponse<AuthResponse>>();

        using var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
        {
            Content = JsonContent.Create(new { refreshToken = reg2!.Data!.RefreshToken })
        };
        logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reg1!.Data!.Token);
        var logout = await fixture.Client.SendAsync(logoutReq);
        logout.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PasswordReset_ShouldRevokeActiveRefreshTokens()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.com";
        const string password = "StrongPass1!";
        const string newPassword = "NewStrongPass1!";
        var reg = await (await fixture.Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterCommand(email, password, "Reset", "User")))
            .Content.ReadFromJsonAsync<TestApiResponse<AuthResponse>>();
        var refresh = reg!.Data!.RefreshToken;

        // Generate reset token directly via Identity to avoid parsing email body.
        string resetToken;
        Guid userId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ECommerce.Infrastructure.Identity.AppUser>>();
            var user = await userManager.FindByEmailAsync(email);
            user.Should().NotBeNull();
            userId = user!.Id;
            resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        }

        var reset = await fixture.Client.PostAsJsonAsync("/api/v1/auth/reset-password", new { email, token = resetToken, newPassword });
        reset.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshAfter = await fixture.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh });
        refreshAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConfirmEmail_ShouldSucceed_WithValidToken_AndBeIdempotent()
    {
        var email = $"confirm-ok-{Guid.NewGuid():N}@example.com";
        const string password = "StrongPass1!";
        await fixture.Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterCommand(email, password, "Confirm", "Ok"));

        Guid userId;
        string token;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ECommerce.Infrastructure.Identity.AppUser>>();
            var user = await userManager.FindByEmailAsync(email);
            user.Should().NotBeNull();
            userId = user!.Id;
            token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        }

        var confirm = await fixture.Client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmAgain = await fixture.Client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token });
        confirmAgain.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConfirmEmail_ShouldRejectInvalidToken()
    {
        var email = $"confirm-bad-{Guid.NewGuid():N}@example.com";
        const string password = "StrongPass1!";
        await fixture.Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterCommand(email, password, "Confirm", "Bad"));

        Guid userId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ECommerce.Infrastructure.Identity.AppUser>>();
            var user = await userManager.FindByEmailAsync(email);
            userId = user!.Id;
        }

        var confirm = await fixture.Client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token = "invalid-token" });
        confirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_ShouldRejectExpiredToken()
    {
        var email = $"expired-refresh-{Guid.NewGuid():N}@example.com";
        const string password = "StrongPass1!";
        var reg = await (await fixture.Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterCommand(email, password, "Expired", "Refresh")))
            .Content.ReadFromJsonAsync<TestApiResponse<AuthResponse>>();
        var refresh = reg!.Data!.RefreshToken;

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refresh)));
            var token = await db.RefreshTokens.IgnoreQueryFilters().FirstAsync(x => x.TokenHash == hash);
            db.Entry(token).Property(nameof(RefreshToken.ExpiresAtUtc)).CurrentValue = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        var res = await fixture.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ShouldBeConcurrencySafe_ForSameToken()
    {
        var email = $"concurrent-refresh-{Guid.NewGuid():N}@example.com";
        const string password = "StrongPass1!";
        var reg = await (await fixture.Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterCommand(email, password, "Concurrent", "Refresh")))
            .Content.ReadFromJsonAsync<TestApiResponse<AuthResponse>>();
        var refresh = reg!.Data!.RefreshToken;

        var t1 = fixture.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh });
        var t2 = fixture.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh });
        var results = await Task.WhenAll(t1, t2);

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(1);
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
