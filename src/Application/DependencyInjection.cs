using Application.Abstractions.PreProcessors;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Application.Services.AI;
using Application.Services.AI.PayloadBuilders;
using Application.Services.AI.Streaming;
using Application.Services.Chat;
using Application.Services.Files;
using Application.Services.Helpers;
using Application.Services.Infrastructure;
using Application.Services.Messaging;
using Application.Services.Plugins;
using Application.Services.TokenUsage;

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
        services.AddScoped<IStreamChunkParser, OpenAiStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, AnthropicStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, GeminiStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, DeepseekStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, AimlStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, GrokStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, QwenStreamChunkParser>();

        services.AddScoped<MultimodalContentParser>();

        services.AddScoped<IOpenAiPayloadBuilder, OpenAiPayloadBuilder>();
        services.AddScoped<IAnthropicPayloadBuilder, AnthropicPayloadBuilder>();
        services.AddScoped<IGeminiPayloadBuilder, GeminiPayloadBuilder>();
        services.AddScoped<IDeepSeekPayloadBuilder, DeepSeekPayloadBuilder>();
        services.AddScoped<IAimlFluxPayloadBuilder, AimlFluxPayloadBuilder>();
        services.AddScoped<IGrokPayloadBuilder, GrokPayloadBuilder>();
        services.AddScoped<IQwenPayloadBuilder, QwenPayloadBuilder>();
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
        
        services.AddGlobalPreProcessors();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        return services;
    }
}