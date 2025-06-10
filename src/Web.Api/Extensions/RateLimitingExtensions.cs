using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Web.Api.Extensions;

/// <summary>
/// Extension methods for setting up rate limiting middleware in an application.
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// Adds rate limiting services to the service collection.
    /// </summary>
    public static IServiceCollection AddApplicationRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // Global IP-based rate limiting - prevent DoS attacks
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 200,  // Max 200 requests
                        Window = TimeSpan.FromMinutes(1) // Per minute
                    }));
            
            // Per-user rate limiting for authenticated users
            options.AddPolicy("per-user", httpContext =>
            {
                var userId = httpContext.User.Identity?.Name;
                if (string.IsNullOrEmpty(userId))
                {
                    // If not authenticated, fall back to IP-based limiting
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: partition => new FixedWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            PermitLimit = 100,   // Max 100 requests
                            Window = TimeSpan.FromMinutes(1) // Per minute
                        });
                }
                
                // For authenticated users, use their user ID as the partition key
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: userId,
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 500,   // Max 500 requests
                        Window = TimeSpan.FromMinutes(1) // Per minute
                    });
            });
            
            // Add specific limiters for sensitive endpoints
            options.AddPolicy("login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 10,   // Only 10 login attempts
                        Window = TimeSpan.FromMinutes(5) // Per 5 minute window
                    }));
                    
            // Add specific limiters for password-reset endpoints
            options.AddPolicy("password-reset", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 5,    // Only 5 password reset attempts
                        Window = TimeSpan.FromMinutes(10) // Per 10 minute window
                    }));
                    
            // Add specific limiters for AI completion endpoints
            options.AddPolicy("ai-completion", httpContext =>
            {
                var userId = httpContext.User.Identity?.Name;
                if (string.IsNullOrEmpty(userId))
                {
                    // If not authenticated, use IP-based limiting
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: partition => new FixedWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            PermitLimit = 20,   // 20 AI completions
                            Window = TimeSpan.FromMinutes(5) // Per 5 minute window
                        });
                }
                
                // For authenticated users, use their user ID
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: userId,
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 50,   // 50 AI completions
                        Window = TimeSpan.FromMinutes(5) // Per 5 minute window
                    });
            });
            
            // Add handler for when rate limit is hit
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                
                var userId = context.HttpContext.User.Identity?.Name;
                var ipAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString();
                var logMessage = $"Rate limit exceeded - User: {userId ?? "anonymous"}, IP: {ipAddress}, Endpoint: {context.HttpContext.Request.Path}";
                
                await context.HttpContext.Response.WriteAsJsonAsync(new 
                { 
                    error = "Too many requests. Please try again later.",
                    retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? (double?)retryAfter.TotalSeconds : null
                }, token);
            };
        });

        return services;
    }
}
