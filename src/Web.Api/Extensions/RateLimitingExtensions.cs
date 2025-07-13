using System.Threading.RateLimiting;

namespace Web.Api.Extensions;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApplicationRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 200,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            options.AddPolicy("per-user", httpContext =>
            {
                var userId = httpContext.User.Identity?.Name;
                if (string.IsNullOrEmpty(userId))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: partition => new FixedWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            PermitLimit = 100,
                            Window = TimeSpan.FromMinutes(1)
                        });
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: userId,
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 500,
                        Window = TimeSpan.FromMinutes(1)
                    });
            });

            options.AddPolicy("login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5)
                    }));

            options.AddPolicy("password-reset", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(10)
                    }));

            options.AddPolicy("ai-completion", httpContext =>
            {
                var userId = httpContext.User.Identity?.Name;
                if (string.IsNullOrEmpty(userId))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: partition => new FixedWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            PermitLimit = 20,
                            Window = TimeSpan.FromMinutes(5)
                        });
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: userId,
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 50,
                        Window = TimeSpan.FromMinutes(5)
                    });
            });

            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";

                var userId = context.HttpContext.User.Identity?.Name;
                var ipAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString();
                var logMessage =
                    $"Rate limit exceeded - User: {userId ?? "anonymous"}, IP: {ipAddress}, Endpoint: {context.HttpContext.Request.Path}";

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = "Too many requests. Please try again later.",
                    retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                        ? (double?)retryAfter.TotalSeconds
                        : null
                }, token);
            };
        });

        return services;
    }
}