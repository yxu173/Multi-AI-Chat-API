using Application.Abstractions.Interfaces;
using Application.Abstractions.PreProcessors;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Application.Services.AI;
using Application.Services.AI.Builders;
using Application.Services.AI.Interfaces;
using Application.Services.AI.PayloadBuilders;
using Application.Services.AI.RequestHandling;
using Application.Services.AI.RequestHandling.Interfaces;
using Application.Services.AI.Streaming;
using Application.Services.Chat;
using Application.Services.Chat.Commands;
using Application.Services.Files;
using Application.Services.Helpers;
using Application.Services.Infrastructure;
using Application.Services.Messaging;
using Application.Services.Messaging.Handlers;
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



        services.AddScoped<IHistoryProcessor, HistoryProcessor>();
        services.AddScoped<IFileAttachmentService, FileAttachmentService>();
        services.AddScoped<IToolDefinitionService, ToolDefinitionService>();
        services.AddScoped<IAiRequestHandler, AiRequestHandler>();


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

        services.AddScoped<OpenAiPayloadBuilder>();
        services.AddScoped<AnthropicPayloadBuilder>();
        services.AddScoped<GeminiPayloadBuilder>();
        services.AddScoped<DeepSeekPayloadBuilder>();
        services.AddScoped<AimlFluxPayloadBuilder>();
        services.AddScoped<GrokPayloadBuilder>();
        services.AddScoped<QwenPayloadBuilder>();
        services.AddScoped<ImagenPayloadBuilder>();

        services.AddScoped<IAiRequestBuilder, OpenAiPayloadBuilder>();
        services.AddScoped<IAiRequestBuilder, AnthropicPayloadBuilder>();
        services.AddScoped<IAiRequestBuilder, GeminiPayloadBuilder>();
        services.AddScoped<IAiRequestBuilder, DeepSeekPayloadBuilder>();
        services.AddScoped<IAiRequestBuilder, AimlFluxPayloadBuilder>();
        services.AddScoped<IAiRequestBuilder, GrokPayloadBuilder>();
        services.AddScoped<IAiRequestBuilder, QwenPayloadBuilder>();
        services.AddScoped<IAiRequestBuilder, ImagenPayloadBuilder>();

        services.AddScoped<IPayloadBuilderFactory, PayloadBuilderFactory>();

        services.AddScoped<SendUserMessageCommand>();
        services.AddScoped<EditUserMessageCommand>();
        services.AddScoped<RegenerateAiResponseCommand>();

        services.AddScoped<ChatService>();
        services.AddScoped<ChatSessionService>();
        services.AddScoped<MessageService>();
        services.AddScoped<TokenUsageService>();
        services.AddScoped<PluginService>();
        
        
        services.AddScoped<IResponseHandler, ImageResponseHandler>();
        services.AddScoped<IResponseHandler, ToolCallStreamingResponseHandler>();
        services.AddScoped<IResponseHandler, TextStreamingResponseHandler>();
        services.AddScoped<IMessageStreamer, MessageStreamer>();
        services.AddScoped<IAiMessageFinalizer, AiMessageFinalizer>();
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