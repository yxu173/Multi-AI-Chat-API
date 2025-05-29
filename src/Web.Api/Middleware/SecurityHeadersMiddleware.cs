using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Web.Api.Middleware;

/// <summary>
/// Middleware to add security headers to all responses
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    
    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        // Add security headers to all responses
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "DENY");
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        context.Response.Headers.Append("Content-Security-Policy", 
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline'; " +
            "style-src 'self' 'unsafe-inline'; " + 
            "img-src 'self' data:; " +
            "connect-src 'self' wss: ws:;");
        context.Response.Headers.Append("Permissions-Policy", 
            "accelerometer=(), camera=(), geolocation=(), gyroscope=(), " +
            "magnetometer=(), microphone=(), payment=(), usb=()");

        await _next(context);
    }
}

/// <summary>
/// Extension method to add the security headers middleware to the request pipeline
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
