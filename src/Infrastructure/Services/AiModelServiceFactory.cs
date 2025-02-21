using System;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services;

public class AiModelServiceFactory : IAiModelServiceFactory
{
    private readonly IServiceProvider _serviceProvider;

    public AiModelServiceFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public IAiModelService GetService(ModelType modelType)
    {
        return modelType switch
        {
            ModelType.ChatGPT => _serviceProvider.GetService<ChatGptService>(),
            ModelType.Claude => _serviceProvider.GetService<ClaudeService>(),
            ModelType.DeepSeek => _serviceProvider.GetService<DeepSeekService>(),
            ModelType.Gemini => _serviceProvider.GetService<GeminiService>(),
            _ => throw new NotSupportedException($"Model type {modelType} not supported.")
        };
    }
}