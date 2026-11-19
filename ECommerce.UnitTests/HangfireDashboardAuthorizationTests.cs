using System.Security.Claims;
using ECommerce.API.Hangfire;
using FluentAssertions;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace ECommerce.UnitTests;

public class HangfireDashboardAuthorizationTests
{
    private readonly HangfireDashboardAuthorizationFilter _filter = new();

    [Fact]
    public void Authorize_ShouldAllowAuthenticatedAdmin()
    {
        var context = CreateContext(authenticated: true, "Admin");

        _filter.Authorize(context).Should().BeTrue();
    }

    [Fact]
    public void Authorize_ShouldDenyAuthenticatedNonAdmin()
    {
        var context = CreateContext(authenticated: true);

        _filter.Authorize(context).Should().BeFalse();
    }

    [Fact]
    public void Authorize_ShouldDenyAnonymousUser()
    {
        var context = CreateContext(authenticated: false, "Admin");

        _filter.Authorize(context).Should().BeFalse();
    }

    private static DashboardContext CreateContext(bool authenticated, params string[] roles)
    {
        var identity = new ClaimsIdentity(authenticated ? "Test" : null);

        if (authenticated)
        {
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        }

        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };

        return new AspNetCoreDashboardContext(
            Substitute.For<JobStorage>(),
            new DashboardOptions(),
            httpContext);
    }
}
