using System.Text.Json;
using Application.Services.AI.Builders;
using Application.Services.AI.Interfaces;
using Application.Services.AI.RequestHandling.Interfaces;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI;

public record FunctionDefinitionDto(
    string Name,
    string? Description,
    object? Parameters
);

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
    List<FunctionDefinitionDto>? Functions = null,
    string? FunctionCall = null
);

public class AiRequestHandler : IAiRequestHandler
{
    private readonly IPayloadBuilderFactory _payloadBuilderFactory;
    private readonly IHistoryProcessor _historyProcessor;
    private readonly IToolDefinitionService _toolDefinitionService;
    private readonly ILogger<AiRequestHandler> _logger;

    public AiRequestHandler(
        IPayloadBuilderFactory payloadBuilderFactory,
        IHistoryProcessor historyProcessor,
        IToolDefinitionService toolDefinitionService,
        ILogger<AiRequestHandler> logger)
    {
        _payloadBuilderFactory = payloadBuilderFactory ?? throw new ArgumentNullException(nameof(payloadBuilderFactory));
        _historyProcessor = historyProcessor ?? throw new ArgumentNullException(nameof(historyProcessor));
        _toolDefinitionService = toolDefinitionService ?? throw new ArgumentNullException(nameof(toolDefinitionService));
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

        List<object>? toolDefinitions = null;
        var modelType = updatedContext.SpecificModel.ModelType;
        bool modelMightSupportTools = modelType is ModelType.OpenAi or ModelType.Anthropic or 
                                              ModelType.Gemini or ModelType.DeepSeek or 
                                              ModelType.Grok or ModelType.Qwen;
        
        // Always check for available tools when the model supports them
        if (modelMightSupportTools)
        {
            _logger.LogDebug("Model {ModelType} supports tools and functions are defined", modelType);
            toolDefinitions = await _toolDefinitionService.GetToolDefinitionsAsync(
                updatedContext.UserId, modelType, cancellationToken);
                
          
            // If we have tool definitions, make sure we set FunctionCall to "auto" to enable them
            if (toolDefinitions != null && toolDefinitions.Any())
            {
                _logger.LogInformation("Found {Count} plugin tools available for this request", toolDefinitions.Count);
                
                // Initialize Functions if not already set and set FunctionCall
                if (updatedContext.Functions == null)
                {
                    _logger.LogDebug("Initializing Functions property in context");
                    updatedContext = updatedContext with { Functions = new List<FunctionDefinitionDto>(), FunctionCall = "auto" };
                }
            }
            
            // Special handling for Grok and Qwen models
            if ((modelType == ModelType.Grok || modelType == ModelType.Qwen) && 
                toolDefinitions != null && toolDefinitions.Any())
            {
                try
                {
                    var functions = new List<FunctionDefinitionDto>();
                    
                    foreach (var tool in toolDefinitions)
                    {
                        string json = JsonSerializer.Serialize(tool);
                        using JsonDocument doc = JsonDocument.Parse(json);
                        
                        string? name = null;
                        string? description = null;
                        object? parameters = null;
                        
                        if (doc.RootElement.TryGetProperty("function", out var functionElement))
                        {
                            if (functionElement.TryGetProperty("name", out var nameElement))
                            {
                                name = nameElement.GetString();
                            }
                            
                            if (functionElement.TryGetProperty("description", out var descElement))
                            {
                                description = descElement.GetString();
                            }
                            
                            if (functionElement.TryGetProperty("parameters", out var paramsElement))
                            {
                                parameters = JsonSerializer.Deserialize<object>(paramsElement.GetRawText());
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(name))
                        {
                            functions.Add(new FunctionDefinitionDto(name, description, parameters));
                        }
                    }
                    
                    if (functions.Any())
                    {
                        _logger.LogDebug("Adding {Count} functions to context for {ModelType}", functions.Count, modelType);
                        updatedContext = updatedContext with { Functions = functions, FunctionCall = "auto" };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error converting tool definitions to function definitions for {ModelType}", modelType);
                }
            }
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