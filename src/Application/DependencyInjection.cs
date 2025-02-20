using Application.Abstractions.Behaviors;
using Application.Abstractions.Interfaces;
using Application.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<ClaudeService>();
        services.AddSingleton<ChatGPTService>();
        services.AddScoped<IAiServiceFactory, AiServiceFactory>();

        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);

            config.AddOpenBehavior(typeof(RequestLoggingPipelineBehavior<,>));
             config.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
        });

         services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        return services;
    }
}
