using Hangfire.Dashboard;

namespace Kariyer.Mail.Api.Common.Web.Filters;

public class ProxySafeAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        return true;
    }
}