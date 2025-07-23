using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Abstractions.Interfaces;
using Application.Services.Messaging;
using Application.Services.Plugins;
using Domain.Aggregates.Chats;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using Application.Notifications;
using FastEndpoints;

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
        IAiModelService aiService,
        ParsedToolCall toolCall,
        Guid chatSessionId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to execute tool: {ToolName} with ID: {ToolCallId}", toolCall.Name, toolCall.Id);

        var definitions = _pluginExecutorFactory.GetAllPluginDefinitions();
        var definition = definitions.FirstOrDefault(d => d.Name == toolCall.Name);

        if (definition == null)
        {
            var errorMsg = $"Tool with name '{toolCall.Name}' not found.";
            _logger.LogWarning(errorMsg);
            var errorContext = new ToolResultFormattingContext(toolCall.Id, toolCall.Name, errorMsg, false);
            return await aiService.FormatToolResultAsync(errorContext, cancellationToken);
        }

        _logger.LogInformation("Attempting to parse arguments for tool {ToolName} (ID: {ToolCallId}). Arguments: {Arguments}", 
            toolCall.Name, toolCall.Id, toolCall.Arguments);
        
        var arguments = ParseArguments(toolCall.Arguments);

        if (definition.Name == "jina_deepsearch")
        {
            _logger.LogInformation("Using streaming execution for jina_deepsearch plugin.");
            try
            {
                await new DeepSearchStartedNotification(chatSessionId, "Performing deep search...").PublishAsync(Mode.WaitForNone, cancellationToken);

                var pluginInstance = _pluginExecutorFactory.GetPlugin(definition.Id);
                var jinaPlugin = pluginInstance as dynamic;

                string query = arguments?["query"]?.GetValue<string>() ?? string.Empty;
                var fullResult = await jinaPlugin.StreamWithNotificationAsync(
                    query,
                    chatSessionId,
                    (Func<string, Guid, Task>) (async (chunk, id) => await new DeepSearchChunkReceivedNotification(id, chunk).PublishAsync(Mode.WaitForNone, cancellationToken)),
                    cancellationToken
                );
                
                await new DeepSearchResultsNotification(chatSessionId, fullResult).PublishAsync(Mode.WaitForNone, cancellationToken);
                var successContext = new ToolResultFormattingContext(toolCall.Id, toolCall.Name, fullResult, true);
                return await aiService.FormatToolResultAsync(successContext, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during streaming execution of jina_deepsearch plugin.");
                await new DeepSearchErrorNotification(chatSessionId, "Deep search failed during tool execution.").PublishAsync(Mode.WaitForNone, cancellationToken);
                var errorContext = new ToolResultFormattingContext(toolCall.Id, toolCall.Name, $"Error: {ex.Message}", false);
                return await aiService.FormatToolResultAsync(errorContext, cancellationToken);
            }
        }
        else
        {
            _logger.LogInformation("Executing plugin {PluginName} (ID: {PluginId}) for tool call {ToolCallId}", 
                toolCall.Name, definition.Id, toolCall.Id);
            var result = await _pluginService.ExecutePluginByIdAsync(definition.Id, arguments, cancellationToken);
            
            var resultContext = new ToolResultFormattingContext(toolCall.Id, toolCall.Name, result.Result, result.Success);
            return await aiService.FormatToolResultAsync(resultContext, cancellationToken);
        }
    }

    private JsonObject? ParseArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<JsonObject>(argumentsJson);
        }
        catch (JsonException)
        {
            _logger?.LogWarning("Tool result content was not valid JSON: {Content}", argumentsJson);
            return null;
        }
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