using Application.Abstractions.Interfaces;
using Application.Services.Helpers;
using Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace Application.Services.PayloadBuilders;

public class PayloadBuilderFactory : IPayloadBuilderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PayloadBuilderFactory> _logger;

    public PayloadBuilderFactory(IServiceProvider serviceProvider, ILogger<PayloadBuilderFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public IOpenAiPayloadBuilder CreateOpenAiBuilder()
    {
        return _serviceProvider.GetRequiredService<IOpenAiPayloadBuilder>();
    }

    public IAnthropicPayloadBuilder CreateAnthropicBuilder()
    {
        return _serviceProvider.GetRequiredService<IAnthropicPayloadBuilder>();
    }

    public IGeminiPayloadBuilder CreateGeminiBuilder()
    {
        return _serviceProvider.GetRequiredService<IGeminiPayloadBuilder>();
    }

    public IDeepSeekPayloadBuilder CreateDeepSeekBuilder()
    {
        return _serviceProvider.GetRequiredService<IDeepSeekPayloadBuilder>();
    }

    public IAimlFluxPayloadBuilder CreateAimlFluxBuilder()
    {
        return _serviceProvider.GetRequiredService<IAimlFluxPayloadBuilder>();
    }

    public IPayloadBuilder CreateImagenBuilder()
    {
        return _serviceProvider.GetRequiredService<ImagenPayloadBuilder>();
    }

    public IGrokPayloadBuilder CreateGrokBuilder()
    {
        return _serviceProvider.GetRequiredService<IGrokPayloadBuilder>();
    }

    public BasePayloadBuilder GetBuilder(ModelType modelType)
    {
        _logger.LogDebug("Getting payload builder for model type {ModelType}", modelType);
        return modelType switch
        {
            ModelType.OpenAi => (BasePayloadBuilder)CreateOpenAiBuilder(),
            ModelType.Anthropic => (BasePayloadBuilder)CreateAnthropicBuilder(),
            ModelType.Gemini => (BasePayloadBuilder)CreateGeminiBuilder(),
            ModelType.DeepSeek => (BasePayloadBuilder)CreateDeepSeekBuilder(),
            ModelType.AimlFlux => (BasePayloadBuilder)CreateAimlFluxBuilder(),
            ModelType.Imagen => (BasePayloadBuilder)CreateImagenBuilder(),
            ModelType.Grok => (BasePayloadBuilder)CreateGrokBuilder(),
            _ => throw new NotSupportedException($"Payload builder for model type {modelType} is not supported.")
        };
    }
} 