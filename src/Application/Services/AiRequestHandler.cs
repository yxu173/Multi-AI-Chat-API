using Application.Abstractions.Interfaces;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using Domain.Repositories;
using Application.Services.PayloadBuilders;

namespace Application.Services;

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
    string? SafetyTolerance = null
);

public interface IAiRequestHandler
{
    Task<AiRequestPayload> PrepareRequestPayloadAsync(
        AiRequestContext context,
        CancellationToken cancellationToken = default);
}

public class AiRequestHandler : IAiRequestHandler
{
    private readonly ILogger<AiRequestHandler> _logger;
    private readonly IPayloadBuilderFactory _payloadBuilderFactory;
    private readonly IPluginExecutorFactory _pluginExecutorFactory;
    private readonly IChatSessionPluginRepository _chatSessionPluginRepository;

    public AiRequestHandler(
        IPayloadBuilderFactory payloadBuilderFactory,
        IPluginExecutorFactory pluginExecutorFactory,
        IChatSessionPluginRepository chatSessionPluginRepository,
        ILogger<AiRequestHandler> logger)
    {
        _payloadBuilderFactory = payloadBuilderFactory ?? throw new ArgumentNullException(nameof(payloadBuilderFactory));
        _pluginExecutorFactory = pluginExecutorFactory ?? throw new ArgumentNullException(nameof(pluginExecutorFactory));
        _chatSessionPluginRepository = chatSessionPluginRepository ?? throw new ArgumentNullException(nameof(chatSessionPluginRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AiRequestPayload> PrepareRequestPayloadAsync(AiRequestContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.ChatSession);
        ArgumentNullException.ThrowIfNull(context.SpecificModel);
        ArgumentNullException.ThrowIfNull(context.History);

        var modelType = context.SpecificModel.ModelType;
        var chatId = context.ChatSession.Id;

        bool modelMightSupportTools = modelType is ModelType.OpenAi or ModelType.Anthropic or ModelType.Gemini or ModelType.DeepSeek;
        List<object>? toolDefinitions = null;

        if (modelMightSupportTools)
        {
            var activePlugins = await _chatSessionPluginRepository.GetActivatedPluginsAsync(chatId, cancellationToken);
            var activePluginIds = activePlugins.Select(p => p.PluginId).ToList();

            if (activePluginIds.Any())
            {
                _logger?.LogInformation("Found {Count} active plugins for ChatSession {ChatId}", activePluginIds.Count, chatId);
                toolDefinitions = GetToolDefinitionsForPayload(modelType, activePluginIds);
            }
            else
            {
                _logger?.LogInformation("No active plugins found for ChatSession {ChatId}", chatId);
            }
        }
        else
        {
             _logger?.LogDebug("Tool support check skipped for model type {ModelType}", modelType);
        }

        try
        {
            AiRequestPayload payload = modelType switch
            {
                ModelType.OpenAi => _payloadBuilderFactory.CreateOpenAiBuilder().PreparePayload(context, toolDefinitions),
                ModelType.Anthropic => _payloadBuilderFactory.CreateAnthropicBuilder().PreparePayload(context, toolDefinitions),
                ModelType.Gemini => await _payloadBuilderFactory.CreateGeminiBuilder().PreparePayloadAsync(context, toolDefinitions, cancellationToken),
                ModelType.DeepSeek => await _payloadBuilderFactory.CreateDeepSeekBuilder().PreparePayloadAsync(context, toolDefinitions, cancellationToken),
                ModelType.AimlFlux => _payloadBuilderFactory.CreateAimlFluxBuilder().PreparePayload(context),
                ModelType.Imagen => _payloadBuilderFactory.CreateImagenBuilder().PreparePayload(context),
                _ => throw new NotSupportedException($"Model type {modelType} is not supported for request preparation."),
            };
            return payload;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error preparing payload for {ModelType}", modelType);
            throw;
        }
    }

    private List<object>? GetToolDefinitionsForPayload(ModelType modelType, List<Guid> activePluginIds)
    {
        if (activePluginIds == null || !activePluginIds.Any()) return null;

        var allDefinitions = _pluginExecutorFactory.GetAllPluginDefinitions().ToList();
        if (!allDefinitions.Any())
        {
            _logger?.LogDebug("No plugin definitions found in the factory.");
            return null;
        }

        var activeDefinitions = allDefinitions
            .Where(def => activePluginIds.Contains(def.Id))
            .ToList();

        if (!activeDefinitions.Any())
        {
            _logger?.LogWarning("No matching definitions found in factory for active plugin IDs: {ActiveIds}", string.Join(", ", activePluginIds));
            return null;
        }

        _logger?.LogInformation("Found {DefinitionCount} active plugin definitions to format for {ModelType}.", activeDefinitions.Count, modelType);
        var formattedDefinitions = new List<object>();

        foreach (var def in activeDefinitions)
        {
            if (def.ParametersSchema == null)
            {
                _logger?.LogWarning("Skipping tool definition for {ToolName} ({ToolId}) due to missing parameter schema.", def.Name, def.Id);
                continue;
            }

            try
            {
                switch (modelType)
                {
                    case ModelType.OpenAi:
                        formattedDefinitions.Add(new
                        {
                            type = "function",
                            function = new
                            {
                                name = def.Name,
                                description = def.Description,
                                parameters = def.ParametersSchema
                            }
                        });
                        break;

                    case ModelType.Anthropic:
                        formattedDefinitions.Add(new
                        {
                            name = def.Name,
                            description = def.Description,
                            input_schema = def.ParametersSchema
                        });
                        break;

                    case ModelType.Gemini:
                        formattedDefinitions.Add(new
                        {
                            name = def.Name,
                            description = def.Description,
                            parameters = def.ParametersSchema
                        });
                        break;
                        
                    case ModelType.DeepSeek:
                        _logger?.LogWarning("Tool definition formatting for DeepSeek is not yet defined/supported. Skipping tool: {ToolName}", def.Name);
                        break;

                    default:
                        _logger?.LogWarning("Tool definition requested for provider {ModelType} which may not support the standard format or is unknown. Skipping tool: {ToolName}", modelType, def.Name);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error formatting tool definition for {ToolName} ({ToolId}) for provider {ModelType}", def.Name, def.Id, modelType);
            }
        }

        if (!formattedDefinitions.Any())
        {
            _logger?.LogWarning("No tool definitions could be formatted successfully for {ModelType}.", modelType);
            return null;
        }

        _logger?.LogInformation("Successfully formatted {FormattedCount} tool definitions for {ModelType}.", formattedDefinitions.Count, modelType);
        return formattedDefinitions;
    }
}