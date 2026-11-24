using Hangfire.Dashboard;

namespace ECommerce.API.Hangfire;

/// <summary>
/// The dashboard is a privileged operations surface: it must always require an
/// authenticated principal in the Admin role, in every environment.
/// </summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        return httpContext.User.Identity?.IsAuthenticated == true &&
               httpContext.User.IsInRole("Admin");
    }
}
