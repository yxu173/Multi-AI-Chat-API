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
            // Global rate limiting - prevent DoS attacks
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 200,  // Max 200 requests
                        Window = TimeSpan.FromMinutes(1) // Per minute
                    }));
            
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
            options.AddPolicy("password", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 5,    // Only 5 password reset attempts
                        Window = TimeSpan.FromMinutes(10) // Per 10 minute window
                    }));
                    
            // Add specific limiters for AI completion endpoints to prevent token abuse
            options.AddPolicy("ai-completion", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 50,   // 50 AI completions
                        Window = TimeSpan.FromMinutes(5) // Per 5 minute window
                    }));
            
            // Add handler for when rate limit is hit
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                
                var logMessage = $"Rate limit exceeded for IP: {context.HttpContext.Connection.RemoteIpAddress}, Endpoint: {context.HttpContext.Request.Path}";  
                await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests. Please try again later." }, token);
            };
        });

        return services;
    }
}
