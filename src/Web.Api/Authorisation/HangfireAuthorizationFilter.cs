using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;

namespace Web.Api.Authorisation; // Adjusted namespace

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
      //  var httpContext = context.GetHttpContext();

        // Allow all authenticated users to see the Dashboard (potentially restrict this further based on roles)
      //  return httpContext.User?.Identity?.IsAuthenticated ?? false;

        // Example for allowing specific roles (Uncomment and adjust as needed):
        // return httpContext.User?.IsInRole("Admin") ?? false;

        return true;
    }
} 