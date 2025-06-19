using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Abstractions.Interfaces;
using Application.Services.Messaging;
using Application.Services.Plugins;
using Domain.Aggregates.Chats;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public record ParsedToolCall(string Id, string Name, string Arguments);

public class ToolCallHandler
{
    private readonly ILogger<ToolCallHandler> _logger;
    private readonly PluginService _pluginService;
    private readonly IPluginExecutorFactory _pluginExecutorFactory;

    public ToolCallHandler(
        ILogger<ToolCallHandler> logger,
        PluginService pluginService,
        IPluginExecutorFactory pluginExecutorFactory)
    {
        _logger = logger;
        _pluginService = pluginService;
        _pluginExecutorFactory = pluginExecutorFactory;
    }

    public Guid? FindPluginIdByName(string toolName)
    {
        _logger.LogInformation("Attempting to find plugin ID for tool name: {ToolName}", toolName);
        var definitions = _pluginExecutorFactory.GetAllPluginDefinitions();
        var match = definitions.FirstOrDefault(d => d.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            _logger.LogInformation("Found matching plugin ID {PluginId} for tool name {ToolName}", match.Id, toolName);
            return match.Id;
        }
        _logger.LogWarning("No plugin definition found matching tool name: {ToolName}", toolName);
        return null;
    }

    private JsonElement? TryParseJsonElement(string jsonString)
    {
        if (string.IsNullOrWhiteSpace(jsonString)) return null;
        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            _logger?.LogWarning("Tool result content was not valid JSON: {Content}", jsonString);
            return null;
        }
    }

    public async Task<MessageDto> ExecuteToolCallAsync(
        IAiModelService aiModelService,
        ParsedToolCall toolCall,
        CancellationToken cancellationToken)
    {
        var pluginId = FindPluginIdByName(toolCall.Name);
        
        PluginResult<string> pluginResult;

        if (!pluginId.HasValue)
        {
            _logger.LogError("Could not find plugin matching tool name: {ToolName}", toolCall.Name);
            pluginResult = new PluginResult<string>("", false, $"Plugin '{toolCall.Name}' not found.");
        }
        else
        {
            try
            {
                _logger.LogInformation("Attempting to parse arguments for tool {ToolName} (ID: {ToolCallId}). Arguments: {Arguments}", toolCall.Name, toolCall.Id, toolCall.Arguments);
                var argumentsObject = JsonSerializer.Deserialize<JsonObject>(toolCall.Arguments);

                if (argumentsObject != null)
                {
                    _logger.LogInformation("Executing plugin {PluginName} (ID: {PluginId}) for tool call {ToolCallId}", toolCall.Name, pluginId.Value, toolCall.Id);
                    pluginResult = await _pluginService.ExecutePluginByIdAsync(pluginId.Value, argumentsObject, cancellationToken);
                }
                else
                {
                    pluginResult = new PluginResult<string>("", false, $"Could not parse arguments for tool '{toolCall.Name}'.");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse arguments for tool {ToolName} (ID: {ToolCallId}). Arguments: {Arguments}", toolCall.Name, toolCall.Id, toolCall.Arguments);
                pluginResult = new PluginResult<string>("", false, $"Invalid arguments provided for tool '{toolCall.Name}'.");
            }
        }
        
        string resultString = pluginResult.Success ? pluginResult.Result : $"Error: {pluginResult.ErrorMessage}";
        var formatContext = new ToolResultFormattingContext(toolCall.Id, toolCall.Name, resultString, pluginResult.Success);
        
        return await aiModelService.FormatToolResultAsync(formatContext, cancellationToken);
    }

    public async Task<MessageDto> FormatAiMessageWithToolCallsAsync(ModelType modelType, List<ParsedToolCall> toolCalls)
    {
        _logger.LogInformation("Formatting AI message with {Count} tool calls for model type {ModelType}", toolCalls.Count, modelType);

        object messagePayload;
        bool isAiMessageRole = true;

        switch (modelType)
        {
            case ModelType.OpenAi:
                messagePayload = new
                {
                    role = "assistant",
                    content = string.Empty,
                    tool_calls = toolCalls.Select(tc => new
                    {
                        id = tc.Id,
                        type = "function",
                        function = new
                        {
                            name = tc.Name,
                            arguments = tc.Arguments
                        }
                    }).ToArray()
                };
                break;

            case ModelType.Anthropic:
                messagePayload = new
                {
                    role = "assistant",
                    content = toolCalls.Select(tc => new
                    {
                        type = "tool_use",
                        tool_use = new
                        {
                            id = tc.Id,
                            name = tc.Name,
                            parameters = tc.Arguments
                        }
                    }).ToArray()
                };
                break;

            case ModelType.Gemini:
                messagePayload = new
                {
                    parts = toolCalls.Select(tc => new
                    {
                        functionCall = new
                        {
                            name = tc.Name,
                            args = TryParseJsonElement(tc.Arguments) ?? (object)tc.Arguments
                        }
                    }).ToArray()
                };
                break;

            default:
                _logger.LogError("Cannot format tool call message for unsupported provider: {ModelType}", modelType);
                messagePayload = new { error = $"Unsupported model type: {modelType}" };
                break;
        }

        string contentJson = JsonSerializer.Serialize(messagePayload, new JsonSerializerOptions { WriteIndented = false });
        return new MessageDto(contentJson, isAiMessageRole, Guid.NewGuid());
    }
} 