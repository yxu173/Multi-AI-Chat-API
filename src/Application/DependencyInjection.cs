using Application.Abstractions.Behaviors;
using Application.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure uploads directory
        var uploadsBasePath = configuration["FilesStorage:BasePath"] 
            ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        
        if (!Directory.Exists(uploadsBasePath))
        {
            Directory.CreateDirectory(uploadsBasePath);
        }
        
        services.AddScoped<ChatService>();
        services.AddScoped<ChatSessionService>();
        services.AddScoped<MessageService>();
        services.AddScoped<PluginService>();
        services.AddScoped<TokenUsageService>();
        services.AddScoped<MessageStreamer>();
        services.AddScoped<FileUploadService>(provider => 
            new FileUploadService(
                provider.GetRequiredService<Domain.Repositories.IFileAttachmentRepository>(), 
                uploadsBasePath));
        services.AddSingleton<StreamingOperationManager>();
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);

            config.AddOpenBehavior(typeof(RequestLoggingPipelineBehavior<,>));
            config.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
        });

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<ParallelAiProcessingService>();
        return services;
    }
}