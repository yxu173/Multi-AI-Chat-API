using System.Text.Json;
using System.Text.Json.Serialization;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.OpenApi.Models;
using NSwag;
using Web.Api.Infrastructure;
using Web.Api.NotificationHandlers;

namespace Web.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
        services.AddEndpointsApiExplorer();
        services.AddControllers();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        services.SwaggerDocument(o =>
        {
            o.EnableJWTBearerAuth = true;
            o.DocumentSettings = s =>
            {
                s.DocumentName = "Initial-Release";
                s.Title = "Web API";
                s.Version = "v1.0";
                
                s.AddAuth("Bearer", new()
                {
                    Type = OpenApiSecuritySchemeType.Http,
                    Scheme = JwtBearerDefaults.AuthenticationScheme,
                    BearerFormat = "JWT",
                });
            };
            
        });

    return services;
    }
}