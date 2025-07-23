using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services.AI.RequestHandling.Interfaces;
using Application.Services.AI.Interfaces;
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
    string? FunctionCall = null,
    List<PluginDefinition>? ToolDefinitions = null,
    bool EnableDeepSearch = false
);

public class AiRequestHandler : IAiRequestHandler
{
    private readonly IAiModelServiceFactory _aiModelServiceFactory;
    private readonly IHistoryProcessor _historyProcessor;
    private readonly ILogger<AiRequestHandler> _logger;

    public AiRequestHandler(
        IAiModelServiceFactory aiModelServiceFactory,
        IHistoryProcessor historyProcessor,
        ILogger<AiRequestHandler> logger)
    {
        _aiModelServiceFactory = aiModelServiceFactory ?? throw new ArgumentNullException(nameof(aiModelServiceFactory));
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

        if (toolDefinitions != null && toolDefinitions.Any())
        {
            _logger.LogInformation("Found {Count} plugin tools available for this request", toolDefinitions.Count);
            updatedContext = updatedContext with { FunctionCall = "auto" };
        }

        _logger.LogDebug("Building payload for model {ModelType}", modelType);

        try
        {
            var serviceContext = await _aiModelServiceFactory.GetServiceContextAsync(
                updatedContext.UserId,
                updatedContext.ChatSession.AiModelId,
                updatedContext.ChatSession.AiAgentId,
                cancellationToken);
            var aiService = serviceContext.Service;
            // All provider services now have BuildPayloadAsync
            return await aiService.BuildPayloadAsync(updatedContext, toolDefinitions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating or using provider service for {ModelType}", modelType);
            throw;
        }
    }
}