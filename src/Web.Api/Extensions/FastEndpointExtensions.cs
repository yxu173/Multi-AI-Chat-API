using Application.Abstractions.PreProcessors;
using FastEndpoints;
using FastEndpoints.Swagger;

namespace Web.Api.Extensions;

public static class FastEndpointExtensions
{
    public static IServiceCollection AddFastEndpointsExtensions(this IServiceCollection services)
    {
        services.AddFastEndpoints()
            .SwaggerDocument();

        services.AddSingleton(typeof(IPreProcessor<>), typeof(RequestLoggingPreProcessor<>));
        services.AddSingleton(typeof(IPreProcessor<>), typeof(ValidationPreProcessor<>));
        services.AddSingleton(typeof(IPostProcessor<,>), typeof(RequestLoggingPostProcessor<,>));
        
        return services;
    }
}