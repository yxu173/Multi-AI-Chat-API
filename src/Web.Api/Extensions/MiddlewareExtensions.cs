using Web.Api.Middleware;

namespace Web.Api.Extensions
{
    public static class MiddlewareExtensions
    {
        public static IApplicationBuilder UseRequestContextLogging(this IApplicationBuilder app)
        {
            return app.UseMiddleware<RequestContextLoggingMiddleware>();
        }
    }
}
