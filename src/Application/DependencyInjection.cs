using Application.Abstractions.Interfaces;
using Application.Abstractions.PreProcessors;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Application.Services.AI;
using Application.Services.AI.Builders;
using Application.Services.AI.Interfaces;
using Application.Services.AI.RequestHandling;
using Application.Services.AI.RequestHandling.Interfaces;
using Application.Services.AI.Streaming;
using Application.Services.Files;
using Application.Services.Helpers;
using Application.Services.Infrastructure;
using Application.Services.Messaging;
using Application.Services.Plugins;
using Application.Services.Streaming;
using Application.Services.TokenUsage;
using Application.Services.Files.BackgroundProcessing;
using Application.Services.Utilities;
using Application.Features.Chats.SummarizeHistory;
using Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;
using Application.Services.Resilience;
using Application.Features.Chats.GenerateTitle;

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
        services.AddScoped<ChatTitleGenerator>();
        services.AddScoped<GenerateChatTitleJob>();
        
        services.AddScoped<IStreamingService, StreamingService>();
        services.AddScoped<IStreamingContextService, StreamingContextService>(sp =>
            new StreamingContextService(
                sp.GetRequiredService<IChatSessionRepository>(),
                sp.GetRequiredService<IUserAiModelSettingsRepository>(),
                sp.GetRequiredService<IAiAgentRepository>(),
                sp.GetRequiredService<IToolDefinitionService>(),
                sp.GetRequiredService<IAiModelRepository>()
            )
        );
        services.AddScoped<IStreamingResilienceHandler, StreamingResilienceHandler>();

        services.AddScoped<HistorySummarizationService>();
        services.AddScoped<SummarizeChatHistoryJob>();

        services.AddScoped<ToolCallHandler>();
        services.AddScoped<IStreamChunkParser, OpenAiStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, AnthropicStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, GeminiStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, DeepseekStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, AimlStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, GrokStreamChunkParser>();
        services.AddScoped<IStreamChunkParser, QwenStreamChunkParser>();

        services.AddScoped<MultimodalContentParser>();

        
        services.AddScoped<TokenUsageService>();
        services.AddScoped<PluginService>();
       
        services.AddScoped<IAiMessageFinalizer, AiMessageFinalizer>();
        services.AddScoped<IAiRequestHandler, AiRequestHandler>();

        services.AddScoped(sp =>
        {
            return new FileUploadService(
                sp.GetRequiredService<IFileAttachmentRepository>(),
                sp.GetRequiredService<IFileStorageService>(),
                sp.GetRequiredService<IBackgroundJobClient>(),
                sp.GetRequiredService<ILogger<FileUploadService>>());
        });

        // Streaming Performance Services
        services.AddSingleton<StreamingOperationManager>();
        services.AddSingleton<StreamingPerformanceMonitor>();
        services.AddScoped<GoogleDriveService>();
        services.AddGlobalPreProcessors();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // Background File Processor
        services.AddScoped<IBackgroundFileProcessor, BackgroundFileProcessor>();

        // Register the StreamingOptions
        services.AddOptions<StreamingOptions>()
            .BindConfiguration(StreamingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}