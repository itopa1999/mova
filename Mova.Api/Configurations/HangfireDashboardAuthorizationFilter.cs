using Hangfire.Dashboard;
using Mova.Shared.Constants;

namespace Mova.Api.Configurations;

public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (!httpContext.User.Identity?.IsAuthenticated ?? true)
            return false;

        return httpContext.User.IsInRole(Roles.Customer); // TODO change this later.
    }
}