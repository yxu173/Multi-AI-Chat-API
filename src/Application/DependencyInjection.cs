using Application.Abstractions.Behaviors;
using Application.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.IO;
using Application.Services.PayloadBuilders;
using Application.Services.Streaming;
using Application.Services.Helpers;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        var uploadsBasePath = configuration["FilesStorage:BasePath"]
                              ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");

        if (!Directory.Exists(uploadsBasePath))
        {
            Directory.CreateDirectory(uploadsBasePath);
        }


        services.AddScoped<StreamProcessor>();
        services.AddScoped<ToolCallHandler>();
        services.AddScoped<TokenUsageTracker>();
        services.AddScoped<IStreamChunkParser, OpenAiStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, AnthropicStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, GeminiStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, DeepseekStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, AimlStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, GrokStreamChunkParser>();

        services.AddScoped<MultimodalContentParser>();

        services.AddScoped<IOpenAiPayloadBuilder, OpenAiPayloadBuilder>();
        services.AddScoped<IAnthropicPayloadBuilder, AnthropicPayloadBuilder>();
        services.AddScoped<IGeminiPayloadBuilder, GeminiPayloadBuilder>();
        services.AddScoped<IDeepSeekPayloadBuilder, DeepSeekPayloadBuilder>();
        services.AddScoped<IAimlFluxPayloadBuilder, AimlFluxPayloadBuilder>();
        services.AddScoped<IGrokPayloadBuilder, GrokPayloadBuilder>();
        services.AddScoped<IPayloadBuilderFactory, PayloadBuilderFactory>();
        services.AddTransient<ImagenPayloadBuilder>();

        services.AddScoped<IPayloadBuilder, ImagenPayloadBuilder>();


        services.AddScoped<ChatService>();
        services.AddScoped<ChatSessionService>();
        services.AddScoped<MessageService>();
        services.AddScoped<PluginService>();
        services.AddScoped<TokenUsageService>();
        services.AddScoped<MessageStreamer>();
        services.AddScoped<IAiRequestHandler, AiRequestHandler>();
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
        return services;
    }
}