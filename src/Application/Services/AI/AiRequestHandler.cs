using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services.AI.Builders;
using Application.Services.AI.Interfaces;
using Application.Services.AI.RequestHandling.Interfaces;
using Application.Services.Messaging;
using Domain.Aggregates.AiAgents;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Llms;
using Domain.Aggregates.Users;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI;

public record AiRequestContext(
    Guid UserId,
    ChatSession ChatSession,
    List<MessageDto> History,
    AiAgent? AiAgent,
    UserAiModelSettings? UserSettings,
    AiModel SpecificModel,
    bool? RequestSpecificThinking = null,
    string? ImageSize = null,
    int? NumImages = null,
    string? OutputFormat = null,
    bool? EnableSafetyChecker = null,
    string? SafetyTolerance = null,
    string? FunctionCall = null,
    List<PluginDefinition>? ToolDefinitions = null,
    bool EnableDeepSearch = false
);

public class AiRequestHandler : IAiRequestHandler
{
    private readonly IPayloadBuilderFactory _payloadBuilderFactory;
    private readonly IHistoryProcessor _historyProcessor;
    private readonly ILogger<AiRequestHandler> _logger;

    public AiRequestHandler(
        IPayloadBuilderFactory payloadBuilderFactory,
        IHistoryProcessor historyProcessor,
        ILogger<AiRequestHandler> logger)
    {
        _payloadBuilderFactory = payloadBuilderFactory ?? throw new ArgumentNullException(nameof(payloadBuilderFactory));
        _historyProcessor = historyProcessor ?? throw new ArgumentNullException(nameof(historyProcessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AiRequestPayload> PrepareRequestPayloadAsync(AiRequestContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.ChatSession);
        ArgumentNullException.ThrowIfNull(context.SpecificModel);
        ArgumentNullException.ThrowIfNull(context.History);

        _logger.LogDebug("Processing history for request in chat {ChatId}", context.ChatSession.Id);
        var processedHistory = await _historyProcessor.ProcessAsync(context.History, cancellationToken);
        var updatedContext = context with { History = processedHistory };

        var modelType = updatedContext.SpecificModel.ModelType;
        List<PluginDefinition>? toolDefinitions = updatedContext.ToolDefinitions;

        // If we have tool definitions, make sure we set FunctionCall to "auto" to enable them
        if (toolDefinitions != null && toolDefinitions.Any())
        {
            _logger.LogInformation("Found {Count} plugin tools available for this request", toolDefinitions.Count);
            
            // Set FunctionCall to "auto" to enable tool use
            updatedContext = updatedContext with { FunctionCall = "auto" };
        }

        _logger.LogDebug("Building payload for model {ModelType}", modelType);
        
        try
        {
            IAiRequestBuilder builder = modelType switch
            {
                ModelType.OpenAi => _payloadBuilderFactory.CreateOpenAiBuilder(),
                ModelType.Anthropic => _payloadBuilderFactory.CreateAnthropicBuilder(),
                ModelType.Gemini => _payloadBuilderFactory.CreateGeminiBuilder(),
                ModelType.DeepSeek => _payloadBuilderFactory.CreateDeepSeekBuilder(),
                ModelType.AimlFlux => _payloadBuilderFactory.CreateAimlFluxBuilder(),
                ModelType.Imagen => _payloadBuilderFactory.CreateImagenBuilder(),
                ModelType.Grok => _payloadBuilderFactory.CreateGrokBuilder(),
                ModelType.Qwen => _payloadBuilderFactory.CreateQwenBuilder(),
                _ => throw new NotSupportedException($"Payload builder for model type {modelType} is not supported.")
            };
            
            return await builder.PreparePayloadAsync(updatedContext, toolDefinitions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating or using payload builder for {ModelType}", modelType);
            throw;
        }
    }
}