using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Abstractions.PreProcessors;

public static class GlobalPreProcessorConfiguration
{
    public static IServiceCollection AddGlobalPreProcessors(this IServiceCollection services)
    {
        services.AddSingleton(typeof(IPreProcessor<>), typeof(RequestLoggingPreProcessor<>));
        services.AddSingleton(typeof(IPreProcessor<>), typeof(ValidationPreProcessor<>));
        services.AddSingleton(typeof(IPostProcessor<,>), typeof(RequestLoggingPostProcessor<,>));
        
        return services;
    }
}
