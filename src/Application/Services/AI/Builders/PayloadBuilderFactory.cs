using Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using Application.Services.AI.Interfaces;

namespace Application.Services.AI.Builders;

public class PayloadBuilderFactory : IPayloadBuilderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PayloadBuilderFactory> _logger;

    public PayloadBuilderFactory(IServiceProvider serviceProvider, ILogger<PayloadBuilderFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public IAiRequestBuilder CreateOpenAiBuilder()
    {
        return _serviceProvider.GetRequiredService<OpenAiPayloadBuilder>();
    }

    public IAiRequestBuilder CreateAnthropicBuilder()
    {
        return _serviceProvider.GetRequiredService<AnthropicPayloadBuilder>();
    }

    public IAiRequestBuilder CreateGeminiBuilder()
    {
        return _serviceProvider.GetRequiredService<GeminiPayloadBuilder>();
    }

    public IAiRequestBuilder CreateDeepSeekBuilder()
    {
        return _serviceProvider.GetRequiredService<DeepSeekPayloadBuilder>();
    }

    public IAiRequestBuilder CreateAimlFluxBuilder()
    {
        return _serviceProvider.GetRequiredService<AimlFluxPayloadBuilder>();
    }

    public IAiRequestBuilder CreateImagenBuilder()
    {
        return _serviceProvider.GetRequiredService<ImagenPayloadBuilder>();
    }

    public IAiRequestBuilder CreateGrokBuilder()
    {
        return _serviceProvider.GetRequiredService<GrokPayloadBuilder>();
    }

    public IAiRequestBuilder CreateQwenBuilder()
    {
        return _serviceProvider.GetRequiredService<QwenPayloadBuilder>();
    }

    public IAiRequestBuilder CreateBflApiBuilder()
    {
        return _serviceProvider.GetRequiredService<BflApiPayloadBuilder>();
    }

    public IAiRequestBuilder GetBuilder(ModelType modelType)
    {
        _logger.LogDebug("Getting payload builder for model type {ModelType}", modelType);
        return modelType switch
        {
            ModelType.OpenAi => CreateOpenAiBuilder(),
            ModelType.Anthropic => CreateAnthropicBuilder(),
            ModelType.Gemini => CreateGeminiBuilder(),
            ModelType.Imagen => CreateImagenBuilder(),
            ModelType.DeepSeek => CreateDeepSeekBuilder(),
            ModelType.Grok => CreateGrokBuilder(),
          //  ModelType.AimlFlux => CreateAimlFluxBuilder(),
            ModelType.Qwen => CreateQwenBuilder(),
            ModelType.AimlFlux => CreateBflApiBuilder(),
            _ => throw new NotSupportedException($"Payload builder for model type {modelType} is not supported.")
        };
    }
}