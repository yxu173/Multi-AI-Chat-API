using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Application.Abstractions.Interfaces;
using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Domain.Enums;
using Domain.Common;
using System.Text.Json.Nodes; // For JsonObject
using System.Text.Json.Serialization; // For ReferenceHandler
using System.Collections.Concurrent; // For thread-safe dictionary if needed

namespace Application.Services;

/// <summary>
/// Manages the streaming of AI responses and related operations like token updates and notifications.
/// </summary>

// Define records for tool call information
public record ParsedToolCall(string Id, string Name, string Arguments);
public record StreamProcessingResult(int InputTokens, int OutputTokens, List<ParsedToolCall>? ToolCalls, bool IsComplete);

// Represents a piece of information extracted from a single stream chunk
public record ParsedChunkInfo(
    string? TextDelta = null,
    string? ThinkingDelta = null,
    ToolCallChunk? ToolCallInfo = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    string? FinishReason = null // Added to detect stop reason
);

// Represents partial information about a tool call from a stream chunk
public record ToolCallChunk(
    int Index, // Index within the tool_calls array (for OpenAI)
    string? Id = null,
    string? Name = null,
    string? ArgumentChunk = null,
    bool IsComplete = false // Flag if this chunk signals the end of this specific tool_call structure
);

// Stores the state of an ongoing tool call being parsed from the stream
internal class ToolCallState
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public StringBuilder ArgumentBuffer { get; } = new StringBuilder();
    public bool IsComplete { get; set; } = false;
}

public class MessageStreamer
{
    private readonly IMediator _mediator;
    private readonly IMessageRepository _messageRepository;
    private readonly TokenUsageService _tokenUsageService;
    private readonly StreamingOperationManager _streamingOperationManager;
    private readonly ILogger<MessageStreamer> _logger;
    private readonly IAiRequestHandler _aiRequestHandler;
    private readonly PluginService _pluginService;
    private readonly IPluginExecutorFactory _pluginExecutorFactory;

    public MessageStreamer(
        IMediator mediator,
        IMessageRepository messageRepository,
        TokenUsageService tokenUsageService,
        StreamingOperationManager streamingOperationManager,
        ILogger<MessageStreamer> logger,
        IAiRequestHandler aiRequestHandler,
        PluginService pluginService,
        IPluginExecutorFactory pluginExecutorFactory)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _tokenUsageService = tokenUsageService ?? throw new ArgumentNullException(nameof(tokenUsageService));
        _streamingOperationManager = streamingOperationManager ?? throw new ArgumentNullException(nameof(streamingOperationManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _aiRequestHandler = aiRequestHandler ?? throw new ArgumentNullException(nameof(aiRequestHandler));
        _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));
        _pluginExecutorFactory = pluginExecutorFactory ?? throw new ArgumentNullException(nameof(pluginExecutorFactory));
    }

    /// <summary>
    /// Handles the end-to-end process of streaming an AI response for a given chat context.
    /// </summary>
    public async Task StreamResponseAsync(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        CancellationToken cancellationToken)
    {
        var chatSessionId = requestContext.ChatSession.Id;
        var aiModel = requestContext.SpecificModel;
        var modelType = aiModel.ModelType;
        var userId = requestContext.UserId;
        
        var cts = new CancellationTokenSource();
        _streamingOperationManager.RegisterOperation(aiMessage.Id, cts);
        
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
        var linkedToken = linkedCts.Token;

        int totalInputTokens = 0;
        int totalOutputTokens = 0;
        string finalAiResponseContent = string.Empty;
        StringBuilder? finalAiResponseContentBuilder = null;
        MessageDto? aiMessageToolCallRequestMessage = null;
        List<MessageDto> toolResultMessages = new List<MessageDto>();
        List<MessageDto> originalHistory = new List<MessageDto>(requestContext.History);

        int maxTurns = 5;
        int turn = 0;
        bool aiResponseCompleted = false;

        try
        {
            if (modelType == ModelType.AimlFlux)
            {
                _logger.LogInformation("Using simplified response path for single-response model {ModelType}", modelType);

                var requestPayload = await _aiRequestHandler.PrepareRequestPayloadAsync(requestContext, linkedToken);
                string? markdownResult = null;
                bool completedSuccessfully = false;

                await foreach (var chunk in aiService.StreamResponseAsync(requestPayload, linkedToken))
                {
                    if (linkedToken.IsCancellationRequested) break;

                    if (!string.IsNullOrEmpty(chunk.RawContent))
                    {
                        markdownResult = chunk.RawContent; 
                        _logger.LogInformation("[AimlFlux Path] Received content, Length: {Length}", markdownResult.Length);
                    }
                    
                    if (chunk.IsCompletion)
                    {
                         completedSuccessfully = !string.IsNullOrEmpty(markdownResult);
                         _logger.LogInformation("[AimlFlux Path] Completion chunk received. Success: {Success}", completedSuccessfully);
                         break;
                    }
                    else
                    {
                         _logger.LogWarning("[AimlFlux Path] Received non-completion chunk for message {MessageId}. Content Length: {Length}", aiMessage.Id, chunk.RawContent?.Length ?? 0);
                    }
                }

                if (linkedToken.IsCancellationRequested)
                {
                    aiMessage.AppendContent($"\n[Cancelled]");
                    aiMessage.InterruptMessage();
                }
                else if (completedSuccessfully && markdownResult != null)
                {
                    finalAiResponseContent = markdownResult;
                    aiMessage.UpdateContent(finalAiResponseContent);
                    aiMessage.CompleteMessage();
                    await _mediator.Publish(new MessageChunkReceivedNotification(chatSessionId, aiMessage.Id, finalAiResponseContent), linkedToken);
                }
                else
                {
                    aiMessage.AppendContent($"\n[Failed to get valid image response]");
                    aiMessage.FailMessage();
                    _logger.LogWarning("AIMLAPI response processing failed or resulted in empty/invalid content for message {MessageId}", aiMessage.Id);
                }
            }
            else 
            {
                _logger.LogInformation("Using standard streaming response path for model {ModelType}", modelType);
                finalAiResponseContentBuilder = new StringBuilder();
                
                while (turn < maxTurns && !aiResponseCompleted && !linkedToken.IsCancellationRequested)
                {
                    turn++;
                    _logger.LogInformation("Starting AI interaction turn {Turn} for Message {MessageId}", turn, aiMessage.Id);

                    var historyForThisTurn = new List<MessageDto>(originalHistory);
                    if (aiMessageToolCallRequestMessage != null)
                    {
                        historyForThisTurn.Add(aiMessageToolCallRequestMessage);
                        historyForThisTurn.AddRange(toolResultMessages);
                         _logger.LogDebug("Added previous turn's tool request and {ResultCount} results to history for Turn {Turn}", toolResultMessages.Count, turn);
                    }

                    var currentRequestContext = requestContext with { History = historyForThisTurn };
                    var requestPayload = await _aiRequestHandler.PrepareRequestPayloadAsync(currentRequestContext, linkedToken);

                    _logger.LogInformation("Initiating AI stream request (Turn {Turn}) for message {MessageId}", turn, aiMessage.Id);
                    var rawStream = aiService.StreamResponseAsync(requestPayload, linkedToken);

                    toolResultMessages.Clear();
                    aiMessageToolCallRequestMessage = null;

                    var streamResult = await ProcessAiStreamAsync(
                        rawStream, 
                        modelType, 
                        aiModel.SupportsThinking,
                        aiMessage, 
                        chatSessionId, 
                        (textChunk) => finalAiResponseContentBuilder.Append(textChunk),
                        linkedToken);

                    totalInputTokens += streamResult.InputTokens;
                    totalOutputTokens += streamResult.OutputTokens;

                    if (linkedToken.IsCancellationRequested) break;

                    if (streamResult.ToolCalls?.Any() == true)
                    {
                        _logger.LogInformation("AI requested {ToolCallCount} tool calls (Turn {Turn})", streamResult.ToolCalls.Count, turn);
                        
                        object aiMessagePayload;

                        if (modelType == ModelType.Anthropic) {
                            var contentBlocks = streamResult.ToolCalls.Select(tc => new {
                                type = "tool_use", id = tc.Id, name = tc.Name, input = JsonSerializer.Deserialize<JsonElement>(tc.Arguments)
                            }).ToList<object>();
                            aiMessagePayload = new { role = "assistant", content = contentBlocks };
                        } else if (modelType == ModelType.Gemini) {
                            var functionCallParts = streamResult.ToolCalls.Select(tc => new {
                                functionCall = new { name = tc.Name, args = JsonSerializer.Deserialize<JsonElement>(tc.Arguments) }
                            }).ToList<object>();
                             aiMessagePayload = new { role = "model", parts = functionCallParts };
                        } else {
                             var openAiToolCalls = streamResult.ToolCalls.Select(tc => new {
                                id = tc.Id, type = "function", function = new { name = tc.Name, arguments = tc.Arguments }
                            }).ToList<object>();
                             aiMessagePayload = new { role = "assistant", content = (string?)null, tool_calls = openAiToolCalls };
                        }
                        string aiMessageContent = JsonSerializer.Serialize(aiMessagePayload, new JsonSerializerOptions { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
                        aiMessageToolCallRequestMessage = new MessageDto(aiMessageContent, true, Guid.NewGuid());
                         _logger.LogDebug("Captured AI tool call request message for next turn.");

                        foreach (var toolCall in streamResult.ToolCalls)
                        {
                             var pluginId = FindPluginIdByName(toolCall.Name);
                             MessageDto? toolResultMessage = null;

                             if (!pluginId.HasValue) {
                                _logger.LogError("Could not find plugin matching tool name: {ToolName}", toolCall.Name);
                                var errorResult = new PluginResult("", false, $"Plugin '{toolCall.Name}' not found.");
                                toolResultMessage = FormatToolResultMessage(modelType, toolCall.Id, toolCall.Name, errorResult, aiMessage);
                             } else {
                                JsonObject? argumentsObject = null;
                                try {
                                    argumentsObject = JsonSerializer.Deserialize<JsonObject>(toolCall.Arguments);
                                } catch (JsonException ex) {
                                    _logger?.LogError(ex, "Failed to parse arguments for tool {ToolName} (ID: {ToolCallId}). Arguments: {Arguments}", toolCall.Name, toolCall.Id, toolCall.Arguments);
                                    var errorResult = new PluginResult("", false, $"Invalid arguments provided for tool '{toolCall.Name}'.");
                                    toolResultMessage = FormatToolResultMessage(modelType, toolCall.Id, toolCall.Name, errorResult, aiMessage);
                                }

                                if (argumentsObject != null) {
                                    _logger?.LogInformation("Executing plugin {PluginName} (ID: {PluginId}) for tool call {ToolCallId}", toolCall.Name, pluginId.Value, toolCall.Id);
                                    var pluginResult = await _pluginService.ExecutePluginByIdAsync(pluginId.Value, argumentsObject, linkedToken);
                                    toolResultMessage = FormatToolResultMessage(modelType, toolCall.Id, toolCall.Name, pluginResult, aiMessage);
                                }
                            }
                             if (toolResultMessage != null) {
                                toolResultMessages.Add(toolResultMessage);
                            }
                        }
                    }
                    else if (streamResult.IsComplete)
                    {
                        _logger.LogInformation("AI stream completed without tool calls (Turn {Turn})", turn);
                        aiResponseCompleted = true;
                    }
                    else
                    {
                        _logger.LogWarning("AI stream finished unexpectedly without completion or tool call (Turn {Turn})", turn);
                        aiResponseCompleted = true;
                    }
                }

                _logger.LogInformation("AI interaction loop finished for Message {MessageId}. AI Response Completed: {IsCompleted}", aiMessage.Id, aiResponseCompleted);

                finalAiResponseContent = finalAiResponseContentBuilder?.ToString() ?? ""; 

                if (!string.IsNullOrEmpty(finalAiResponseContent))
                {
                    aiMessage.UpdateContent(finalAiResponseContent); 
                    _logger.LogInformation("Final AI message content updated (Length: {Length})", finalAiResponseContent.Length);
                }
                else if (!aiResponseCompleted && !linkedToken.IsCancellationRequested) {
                    aiMessage.AppendContent("\n[AI response incomplete or ended unexpectedly]");
                    _logger.LogWarning("AI response seems incomplete for message {MessageId} after loop.", aiMessage.Id);
                }
                 
                if (!linkedToken.IsCancellationRequested) {
                    if (aiResponseCompleted && aiMessage.Status != MessageStatus.Interrupted) {
                        aiMessage.CompleteMessage();
                    } else if (!aiResponseCompleted) {
                        aiMessage.InterruptMessage();
                    }
                } else {
                    aiMessage.InterruptMessage();
                }
            }

            var finalUpdateToken = cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken;
            if (aiMessage.Status == MessageStatus.Streaming) {
                 if(linkedToken.IsCancellationRequested) {
                    aiMessage.InterruptMessage();
                 } else {
                     _logger.LogWarning("Message {MessageId} status was still Streaming before final save. Setting to Interrupted.", aiMessage.Id);
                     aiMessage.InterruptMessage();
                 }
             }
            await _messageRepository.UpdateAsync(aiMessage, finalUpdateToken); 
            _logger.LogInformation("Saved final state for message {MessageId} with status {Status}", aiMessage.Id, aiMessage.Status);

            if (aiMessage.Status == MessageStatus.Completed)
            {
                 await _mediator.Publish(new ResponseCompletedNotification(chatSessionId, aiMessage.Id), finalUpdateToken);
            }
            else if (aiMessage.Status == MessageStatus.Interrupted && !cancellationToken.IsCancellationRequested) 
            {
                await _mediator.Publish(new ResponseStoppedNotification(chatSessionId, aiMessage.Id), finalUpdateToken);
            }
             else if (aiMessage.Status == MessageStatus.Failed)
            {
                 await _mediator.Publish(new ResponseStoppedNotification(chatSessionId, aiMessage.Id), finalUpdateToken);
            }
            
            await FinalizeTokenUsage(chatSessionId, aiModel, totalInputTokens, totalOutputTokens, finalUpdateToken);
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
             bool userCancelled = cancellationToken.IsCancellationRequested;
             _logger.LogInformation("Streaming operation cancelled for chat session {ChatSessionId}. Reason: {Reason}", 
                 chatSessionId, userCancelled ? "User Request" : "Internal Stop");
                 
             var stopReason = userCancelled ? "Cancelled by user" : "Stopped internally";
             await _mediator.Publish(new ResponseStoppedNotification(chatSessionId, aiMessage.Id), CancellationToken.None);
             
             if (aiMessage.Status != MessageStatus.Completed && aiMessage.Status != MessageStatus.Interrupted)
             {
                 aiMessage.AppendContent($"\n[{stopReason}]");
                 aiMessage.InterruptMessage(); 
                 await _messageRepository.UpdateAsync(aiMessage, CancellationToken.None); 
             }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AI response streaming for chat session {ChatSessionId}.", chatSessionId);
            if (aiMessage.Status != MessageStatus.Completed && aiMessage.Status != MessageStatus.Interrupted && aiMessage.Status != MessageStatus.Failed) {
                 aiMessage.AppendContent($"\n[Error: {ex.Message}]");
                 aiMessage.FailMessage();
                 await _messageRepository.UpdateAsync(aiMessage, CancellationToken.None);
            }
            await _mediator.Publish(new ResponseStoppedNotification(chatSessionId, aiMessage.Id), CancellationToken.None);
        }
        finally
        { 
            _streamingOperationManager.StopStreaming(aiMessage.Id);
            linkedCts.Dispose(); 
            cts.Dispose();
            _logger.LogInformation("Streaming operation finished or cleaned up for message {MessageId}", aiMessage.Id);
        }
    }

    private async Task<StreamProcessingResult> ProcessAiStreamAsync(
        IAsyncEnumerable<AiRawStreamChunk> rawStream,
        ModelType modelType,
        bool supportsThinking,
        Message aiMessageForNotifications,
        Guid chatSessionId,
        Action<string> appendFinalContentAction,
        CancellationToken cancellationToken)
    {
        var detectedToolCalls = new List<ParsedToolCall>();
        var toolCallStates = new ConcurrentDictionary<int, ToolCallState>();
        
        int latestReportedInputTokens = 0;
        int latestReportedOutputTokens = 0;
        bool streamCompletedNormally = false;
        string? finalFinishReason = null;

        await foreach (var rawChunk in rawStream.WithCancellation(cancellationToken))
        {
             if (cancellationToken.IsCancellationRequested) break;

            if (rawChunk.IsCompletion)
            {
                streamCompletedNormally = true;
                _logger.LogDebug("Stream marked as complete by provider for Message {MessageId}. Captured FinishReason: {StopReason}",
                    aiMessageForNotifications?.Id.ToString() ?? "N/A", finalFinishReason ?? "N/A");
                break;
            }

            _logger.LogTrace("[StreamDebug] Received Raw Chunk ({ModelType}): {RawContent}", modelType, rawChunk.RawContent);

            if (string.IsNullOrEmpty(rawChunk.RawContent))
            {
                _logger.LogTrace("[StreamDebug] Skipping empty raw chunk.");
                continue;
            }

            ParsedChunkInfo parsed = ParseRawChunk(rawChunk.RawContent, modelType);

            _logger.LogTrace("[StreamDebug] Parsed Chunk ({ModelType}): TextDelta='{TextDelta}', ThinkingDelta='{ThinkingDelta}', ToolCallInfo='{ToolCallInfo}', FinishReason='{FinishReason}', InputTokens={InputTokens}, OutputTokens={OutputTokens}",
                modelType, parsed.TextDelta ?? "NULL", parsed.ThinkingDelta ?? "NULL", parsed.ToolCallInfo?.ToString() ?? "NULL", parsed.FinishReason ?? "NULL", parsed.InputTokens?.ToString() ?? "NULL", parsed.OutputTokens?.ToString() ?? "NULL");

            if(parsed.InputTokens.HasValue) latestReportedInputTokens = parsed.InputTokens.Value;
            if(parsed.OutputTokens.HasValue) latestReportedOutputTokens = parsed.OutputTokens.Value;
            if (!string.IsNullOrEmpty(parsed.FinishReason) && finalFinishReason != "tool_calls" && finalFinishReason != "tool_use")
            {
                if (parsed.FinishReason == "tool_calls" || parsed.FinishReason == "tool_use")
                {
                    finalFinishReason = parsed.FinishReason; 
                    _logger.LogDebug("Captured tool-related finish reason: {FinishReason}", finalFinishReason);
                }
                else
                {
                    finalFinishReason = parsed.FinishReason;
                }
            }

            if (parsed.ToolCallInfo != null)
            {
                var info = parsed.ToolCallInfo;
                var state = toolCallStates.GetOrAdd(info.Index, _ => new ToolCallState { Id = info.Id ?? Guid.NewGuid().ToString() });

                if (info.Id != null && state.Id != info.Id && !state.Id.StartsWith("temp_")) {
                    _logger.LogWarning("Tool Call ID provided ({NewId}) differs from initially assigned/found ID ({OldId}) for index {Index}", info.Id, state.Id, info.Index);
                    state.Id = info.Id;
                }
                if (info.Name != null) state.Name = info.Name;
                if (info.ArgumentChunk != null) state.ArgumentBuffer.Append(info.ArgumentChunk);
                
                if (info.IsComplete) {
                    state.IsComplete = true;
                    _logger.LogInformation("Marked tool call state complete for index {Index}", info.Index);
                }

                _logger.LogTrace("Updated tool call state for Index {Index}: Id={Id}, Name={Name}, ArgsBufferLength={Len}",
                    info.Index, state.Id, state.Name, state.ArgumentBuffer.Length);
            }
            else
            {
                if (!string.IsNullOrEmpty(parsed.ThinkingDelta))
                {
                     _logger.LogTrace("[StreamDebug] Publishing ThinkingChunkReceivedNotification with: '{ThinkingDelta}'", parsed.ThinkingDelta);
                     if (aiMessageForNotifications != null) {
                        await _mediator.Publish(new ThinkingChunkReceivedNotification(chatSessionId, aiMessageForNotifications.Id, parsed.ThinkingDelta), cancellationToken);
                     }
                }
                if (!string.IsNullOrEmpty(parsed.TextDelta))
                {
                    appendFinalContentAction(parsed.TextDelta);

                    _logger.LogTrace("[StreamDebug] Publishing MessageChunkReceivedNotification with: '{TextDelta}'", parsed.TextDelta);
                     if (aiMessageForNotifications != null) {
                        await _mediator.Publish(new MessageChunkReceivedNotification(chatSessionId, aiMessageForNotifications.Id, parsed.TextDelta), cancellationToken);
                     }
                }
            }
        }

        if (finalFinishReason == "tool_calls" || finalFinishReason == "tool_use")
        {
             _logger.LogInformation("Finalizing tool calls based on finish_reason='{FinishReason}'", finalFinishReason);
             foreach (var kvp in toolCallStates)
             {
                 var key = kvp.Key;
                 var state = kvp.Value;
                 
                 if (modelType == ModelType.Anthropic && finalFinishReason == "tool_use")
                 {
                     state.IsComplete = true;
                     _logger.LogInformation("Force marking Anthropic tool call as complete based on finish_reason='{FinishReason}'", finalFinishReason);
                 }
                 
                 bool shouldFinalizeTool = 
                     state.IsComplete || 
                     (modelType == ModelType.OpenAi && finalFinishReason == "tool_calls") ||
                     (modelType == ModelType.Gemini && finalFinishReason == "tool_calls");

                 if (shouldFinalizeTool)
                 { 
                     if (!string.IsNullOrEmpty(state.Id) && !string.IsNullOrEmpty(state.Name))
                     {
                         detectedToolCalls.Add(new ParsedToolCall(state.Id, state.Name, state.ArgumentBuffer.ToString()));
                         _logger.LogInformation("Finalized detected tool call: Key={Key}, Id={Id}, Name={Name}, ArgsLength={Len}", key, state.Id, state.Name, state.ArgumentBuffer.Length);
                     } else {
                         _logger.LogWarning("Incomplete tool call state detected at end of stream for key {Key}: Id={Id}, Name={Name}. Discarding.", key, state.Id ?? "<null>", state.Name ?? "<null>");
                     }
                 }
                 else {
                     _logger.LogWarning("Tool call state for key {Key} was not marked complete by the end of the stream (or finish reason mismatch). Discarding.", key);
                 }
             }
        }
        else if (streamCompletedNormally) {
             _logger.LogInformation("Stream finished with reason '{FinishReason}'. No tool calls to finalize.", finalFinishReason ?? "N/A");
        }
        else {
             _logger.LogWarning("Stream processing loop ended without explicit completion signal (likely cancelled).");
        }

        bool isTextResponseComplete = streamCompletedNormally && finalFinishReason != "tool_calls" && finalFinishReason != "tool_use";

        return new StreamProcessingResult(latestReportedInputTokens, latestReportedOutputTokens, detectedToolCalls.Any() ? detectedToolCalls : null, isTextResponseComplete);
    }

    private ParsedChunkInfo ParseRawChunk(string rawJson, ModelType modelType)
    {
        try
        {
            switch (modelType)
            {
                case ModelType.OpenAi:
                    return ParseOpenAiChunk(rawJson);
                case ModelType.Anthropic:
                    return ParseAnthropicChunk(rawJson);
                case ModelType.Gemini:
                    return ParseGeminiChunk(rawJson);
                case ModelType.DeepSeek:
                    return ParseDeepSeekChunk_Basic(rawJson);
                case ModelType.AimlFlux:
                    return ParseAimlApiChunk(rawJson);
                default:
                     _logger.LogWarning("Parsing not implemented for model type {ModelType}", modelType);
                    return new ParsedChunkInfo();
            }
        }
        catch (JsonException jsonEx)
        { 
             _logger.LogError(jsonEx, "Failed to parse AI stream chunk for {ModelType}. RawChunk: {RawChunk}", modelType, rawJson);
            return new ParsedChunkInfo();
        }
        catch (Exception ex)
        { 
            _logger.LogError(ex, "Unexpected error parsing AI stream chunk for {ModelType}. RawChunk: {RawChunk}", modelType, rawJson);
            return new ParsedChunkInfo();
        }
    }

    private ParsedChunkInfo ParseOpenAiChunk(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;
        string? textDelta = null;
        string? finishReason = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];

            if (firstChoice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
            {
                finishReason = reason.GetString();
                _logger?.LogTrace("Parsed finish_reason: {FinishReason}", finishReason);
            }

            if (firstChoice.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(content.GetString()))
                {
                    textDelta = content.GetString();
                }
                else if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
                {
                    _logger?.LogTrace("Found tool_calls array in delta.");
                    foreach (var toolCallChunkElement in toolCalls.EnumerateArray())
                    {
                        if (!toolCallChunkElement.TryGetProperty("index", out var indexElement) || !indexElement.TryGetInt32(out int index))
                        {
                            _logger?.LogWarning("Skipping tool_call chunk without valid index: {ChunkJson}", toolCallChunkElement.GetRawText());
                            continue;
                        }

                        string? id = null;
                        string? name = null;
                        string? argsChunk = null;

                        if (toolCallChunkElement.TryGetProperty("id", out var idElement))
                        {
                            id = idElement.GetString();
                             _logger?.LogTrace("Parsed tool_call id: {Id} for index {Index}", id, index);
                        }

                        if (toolCallChunkElement.TryGetProperty("function", out var function))
                        {
                            if (function.TryGetProperty("name", out var nameElement))
                            {
                                name = nameElement.GetString();
                                 _logger?.LogTrace("Parsed tool_call name: {Name} for index {Index}", name, index);
                            }
                            if (function.TryGetProperty("arguments", out var argsElement))
                            {
                                argsChunk = argsElement.GetString(); 
                                _logger?.LogTrace("Parsed tool_call arguments chunk (length {Length}) for index {Index}", argsChunk?.Length ?? 0, index);
                            }
                        }

                        if (id != null || name != null || argsChunk != null)
                        {
                            toolCallInfo = new ToolCallChunk(index, id, name, argsChunk);
                            if (firstChoice.TryGetProperty("finish_reason", out _)) {
                                _logger?.LogInformation("Marking tool call complete since finish_reason is present");
                            }
                            break;
                        }
                    }
                }
            }
        }

        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object) {
            _logger?.LogTrace("Found top-level usage object.");
            if(usage.TryGetProperty("prompt_tokens", out var pToken) && pToken.ValueKind == JsonValueKind.Number) inputTokens = pToken.GetInt32();
            if(usage.TryGetProperty("completion_tokens", out var cToken) && cToken.ValueKind == JsonValueKind.Number) outputTokens = cToken.GetInt32();
             if(inputTokens.HasValue || outputTokens.HasValue) {
                 _logger?.LogDebug("Parsed final token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
             }
         }

        return new ParsedChunkInfo(
            TextDelta: textDelta, 
            ToolCallInfo: toolCallInfo, 
            FinishReason: finishReason,
            InputTokens: inputTokens,
            OutputTokens: outputTokens
        );
    }

    private ParsedChunkInfo ParseAnthropicChunk(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        string? textDelta = null;
        string? thinkingDelta = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;

        if (root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
        {
            var eventType = typeElement.GetString();

            switch (eventType)
            {
                case "message_start":
                    if (root.TryGetProperty("message", out var messageStart) && messageStart.TryGetProperty("usage", out var usageStart))
                    {
                        if (usageStart.TryGetProperty("input_tokens", out var inTok) && inTok.ValueKind == JsonValueKind.Number)
                            inputTokens = inTok.GetInt32();
                         _logger?.LogDebug("Parsed input tokens from message_start: {InputTokens}", inputTokens);
                    }
                    break;

                case "content_block_delta":
                    if (root.TryGetProperty("index", out var deltaIndexElement) && deltaIndexElement.TryGetInt32(out int deltaIndex))
                    {
                        if (root.TryGetProperty("delta", out var contentDelta) && contentDelta.TryGetProperty("type", out var deltaTypeElement))
                        {
                            string deltaType = deltaTypeElement.GetString() ?? string.Empty;

                            if (deltaType == "text_delta" && contentDelta.TryGetProperty("text", out var textElement))
                            {
                                textDelta = textElement.GetString();
                                _logger?.LogTrace("Parsed text_delta: Index={Index}, Text='{TextDelta}'", deltaIndex, textDelta);
                            }
                            else if (deltaType == "input_json_delta" && contentDelta.TryGetProperty("partial_json", out var argsChunkElement))
                            {
                                toolCallInfo = new ToolCallChunk(deltaIndex, ArgumentChunk: argsChunkElement.GetString());
                                _logger?.LogTrace("Parsed tool argument chunk (input_json_delta): Index={Index}, Length={Length}", deltaIndex, toolCallInfo.ArgumentChunk?.Length ?? 0);
                            }
                        }
                    }
                    else
                    {
                        _logger?.LogWarning("Could not parse content_block_delta details (missing index or delta info) from: {Json}", rawJson);
                    }
                    break;

                case "content_block_start":
                    if (root.TryGetProperty("content_block", out var blockStart) && blockStart.TryGetProperty("type", out var blockType) && blockType.GetString() == "tool_use")
                    {
                        if (blockStart.TryGetProperty("id", out var toolId) && blockStart.TryGetProperty("name", out var toolName) &&
                            root.TryGetProperty("index", out var startIndexElement) && startIndexElement.TryGetInt32(out int startIndex))
                        {
                            toolCallInfo = new ToolCallChunk(startIndex, Id: toolId.GetString(), Name: toolName.GetString());
                            _logger?.LogTrace("Parsed tool_use start: Index={Index}, Id={Id}, Name={Name}", startIndex, toolCallInfo.Id, toolCallInfo.Name);
                        } else {
                            _logger?.LogWarning("Could not parse tool_use start details from: {Json}", rawJson);
                        }
                    }
                    break;

                case "content_block_stop":
                    if (root.TryGetProperty("index", out var stopIndexElement) && stopIndexElement.TryGetInt32(out int stopIndex)) {
                        _logger?.LogTrace("Received content_block_stop for index {Index}", stopIndex);
                    }
                    break;

                case "message_delta":
                    if (root.TryGetProperty("delta", out var messageDelta) && messageDelta.TryGetProperty("stop_reason", out var reason))
                    {
                        finishReason = reason.GetString();
                         _logger?.LogTrace("Parsed finish_reason from message_delta: {FinishReason}", finishReason);
                    }
                    if (root.TryGetProperty("usage", out var usageDelta))
                    {
                        if (usageDelta.TryGetProperty("output_tokens", out var outTok) && outTok.ValueKind == JsonValueKind.Number)
                            outputTokens = outTok.GetInt32();
                         _logger?.LogDebug("Parsed output tokens from message_delta usage: {OutputTokens}", outputTokens);
                    }
                    break;

                case "message_stop":
                     if (root.TryGetProperty("message", out var messageStop) && messageStop.TryGetProperty("stop_reason", out var stopReason)) {
                         finishReason = stopReason.GetString();
                         _logger?.LogDebug("Parsed final finish_reason from message_stop: {FinishReason}", finishReason);
                     }
                      if (root.TryGetProperty("message", out var finalMessage) && finalMessage.TryGetProperty("usage", out var finalUsage))
                      {
                           if(finalUsage.TryGetProperty("input_tokens", out var finalInTok) && finalInTok.ValueKind == JsonValueKind.Number) inputTokens = finalInTok.GetInt32();
                           if(finalUsage.TryGetProperty("output_tokens", out var finalOutTok) && finalOutTok.ValueKind == JsonValueKind.Number) outputTokens = finalOutTok.GetInt32();
                            if(inputTokens.HasValue || outputTokens.HasValue) {
                                _logger?.LogDebug("Parsed final token usage from message_stop: Input={Input}, Output={Output}", inputTokens, outputTokens);
                            }
                      }
                    break;
                 
                case "ping":
                    _logger?.LogTrace("Received Anthropic ping event.");
                    break;
                    
                case "error":
                     if (root.TryGetProperty("error", out var errorDetails))
                     {
                         _logger?.LogError("Anthropic stream reported error: {ErrorJson}", errorDetails.GetRawText());
                         finishReason = "error";
                     }
                     break;

                default:
                    _logger?.LogWarning("Received unhandled Anthropic event type: {EventType}. RawChunk: {RawChunk}", eventType, rawJson);
                    break;
            }
        }
         else
         {
              _logger?.LogWarning("Could not determine event type from Anthropic chunk: {RawChunk}", rawJson);
         }

        return new ParsedChunkInfo(
            TextDelta: textDelta,
            ThinkingDelta: thinkingDelta,
            ToolCallInfo: toolCallInfo,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            FinishReason: finishReason
        );
    }

    private ParsedChunkInfo ParseGeminiChunk(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        string? textDelta = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;
        bool containsFunctionCall = false;

        _logger?.LogInformation("[GeminiDebug] Raw chunk content: {RawContent}", rawJson);

        if (root.TryGetProperty("error", out var errorElement))
        {
            _logger?.LogError("Gemini stream reported error: {ErrorJson}", errorElement.GetRawText());
            return new ParsedChunkInfo(FinishReason: "error");
        }

        if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
        {
            var firstCandidate = candidates[0];
            
            _logger?.LogInformation("[GeminiDebug] Candidate content: {Candidate}", firstCandidate.GetRawText());

            if (firstCandidate.TryGetProperty("finishReason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String)
            {
                finishReason = reasonElement.GetString();
                _logger?.LogInformation("Parsed Gemini finishReason: {FinishReason}", finishReason);
                if (finishReason == "TOOL_CODE" || finishReason == "FUNCTION_CALL") {
                    finishReason = "tool_calls";
                    _logger?.LogInformation("Normalized finishReason to: {NormalizedReason}", finishReason);
                }
            }

            if (firstCandidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0)
            {
                 _logger?.LogInformation("[GeminiDebug] Parts array: {Parts}", parts.GetRawText());
                
                 foreach (var part in parts.EnumerateArray())
                 {
                     _logger?.LogInformation("[GeminiDebug] Processing part: {Part}", part.GetRawText());
                     
                     if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                     {
                         textDelta = textElement.GetString();
                         _logger?.LogInformation("Parsed Gemini text delta: '{TextDelta}'", textDelta);
                     }
                     else if (part.TryGetProperty("functionCall", out var functionCall) && functionCall.ValueKind == JsonValueKind.Object)
                     {
                         _logger?.LogInformation("[GeminiDebug] Found Gemini functionCall: {FunctionCall}", functionCall.GetRawText());
                         containsFunctionCall = true;
                         string? funcName = null;
                         string? funcArgs = null;

                         if (functionCall.TryGetProperty("name", out var nameElement))
                         {
                             funcName = nameElement.GetString();
                             _logger?.LogInformation("Parsed functionCall name: {Name}", funcName);
                         }
                         if (functionCall.TryGetProperty("args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Object)
                         {
                             funcArgs = argsElement.GetRawText(); 
                             _logger?.LogInformation("Parsed functionCall args (raw JSON): {Args}", funcArgs);
                         }

                         if (funcName != null || funcArgs != null)
                         {
                             toolCallInfo = new ToolCallChunk(0, Name: funcName, ArgumentChunk: funcArgs, IsComplete: true);
                             
                             if (string.IsNullOrEmpty(finishReason) || finishReason != "tool_calls")
                             {
                                 finishReason = "tool_calls";
                                 _logger?.LogInformation("Setting finishReason to 'tool_calls' due to detected functionCall");
                             }
                         }
                     }
                 }
            }
        }
        
        if (root.TryGetProperty("usageMetadata", out var usageMetadata))
        {
             _logger?.LogTrace("Found Gemini usageMetadata.");
            if (usageMetadata.TryGetProperty("promptTokenCount", out var pToken) && pToken.ValueKind == JsonValueKind.Number)
            {
                inputTokens = pToken.GetInt32();
            }
            if (usageMetadata.TryGetProperty("candidatesTokenCount", out var cToken) && cToken.ValueKind == JsonValueKind.Number)
            {
                 outputTokens = cToken.GetInt32();
            }
            else if (usageMetadata.TryGetProperty("totalTokenCount", out var tToken) && tToken.ValueKind == JsonValueKind.Number && inputTokens.HasValue)
            {
                 outputTokens = tToken.GetInt32() - inputTokens.Value;
            }
            
             if(inputTokens.HasValue || outputTokens.HasValue) {
                 _logger?.LogDebug("Parsed Gemini token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
             }
        }

        if (containsFunctionCall && finishReason != "tool_calls")
        {
            finishReason = "tool_calls";
            _logger?.LogInformation("Forcing finishReason to 'tool_calls' due to detected functionCall");
        }

        _logger?.LogInformation("[GeminiSummary] Processed chunk: TextDelta={TextDelta}, ToolCallInfo={ToolCallInfo}, FinishReason={FinishReason}", 
            textDelta != null ? $"Length: {textDelta.Length}" : "null",
            toolCallInfo != null ? $"Name: {toolCallInfo.Name}" : "null",
            finishReason);

        return new ParsedChunkInfo(
            TextDelta: textDelta,
            ToolCallInfo: toolCallInfo,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            FinishReason: finishReason
        );
    }

    private ParsedChunkInfo ParseDeepSeekChunk_Basic(string rawJson) { 
        using var doc = JsonDocument.Parse(rawJson);
        string? text = null;
        string? thinking = null;
        ToolCallChunk? toolCallInfo = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? finishReason = null;
        bool containsToolCall = false;

        _logger?.LogInformation("[DeepSeekDebug] Raw chunk content: {RawContent}", rawJson);
        
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0) {
           var firstChoice = choices[0];
           
           if (firstChoice.TryGetProperty("delta", out var delta)) {
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String) {
                    text = content.GetString();
                }
                else if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String) {
                    thinking = rc.GetString();
                }
                
                if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array) {
                    _logger?.LogInformation("[DeepSeekDebug] Found tool_calls array in delta: {ToolCalls}", toolCalls.GetRawText());
                    containsToolCall = true;
                    
                    foreach (var toolCallElement in toolCalls.EnumerateArray()) {
                        if (!toolCallElement.TryGetProperty("index", out var indexElement) || !indexElement.TryGetInt32(out int index)) {
                            _logger?.LogWarning("Skipping tool_call without valid index: {ToolCall}", toolCallElement.GetRawText());
                            continue;
                        }

                        string? id = null;
                        string? name = null;
                        string? argsChunk = null;

                        if (toolCallElement.TryGetProperty("id", out var idElement)) {
                            id = idElement.GetString();
                            _logger?.LogInformation("Parsed tool_call id: {Id} for index {Index}", id, index);
                        }

                        if (toolCallElement.TryGetProperty("function", out var function)) {
                            if (function.TryGetProperty("name", out var nameElement)) {
                                name = nameElement.GetString();
                                _logger?.LogInformation("Parsed tool_call name: {Name} for index {Index}", name, index);
                            }
                            if (function.TryGetProperty("arguments", out var argsElement)) {
                                argsChunk = argsElement.GetString();
                                _logger?.LogInformation("Parsed tool_call arguments: {Args} for index {Index}", argsChunk, index);
                            }
                        }

                        if (id != null || name != null || argsChunk != null) {
                            toolCallInfo = new ToolCallChunk(index, id, name, argsChunk);
                            
                            if (firstChoice.TryGetProperty("finish_reason", out _)) {
                                _logger?.LogInformation("Tool call from DeepSeek is likely complete since finish_reason is present");
                            }
                            
                            break;
                        }
                    }
                    
                    if (toolCallInfo != null && string.IsNullOrEmpty(finishReason)) {
                        finishReason = "tool_calls";
                        _logger?.LogInformation("Setting finishReason to 'tool_calls' due to detected tool call in response");
                    }
                }
            }
            
            if (firstChoice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String) {
                finishReason = reason.GetString();
                _logger?.LogInformation("Parsed DeepSeek finishReason: {FinishReason}", finishReason);
                
                if (finishReason == "function_call" || finishReason == "tool_calls") {
                    finishReason = "tool_calls";
                    _logger?.LogInformation("Normalized finishReason to: {NormalizedReason}", finishReason);
                }
            }
            
            if (doc.RootElement.TryGetProperty("usage", out var usage)) {
                if(usage.TryGetProperty("prompt_tokens", out var pToken) && pToken.ValueKind == JsonValueKind.Number)
                    inputTokens = pToken.GetInt32();
                if(usage.TryGetProperty("completion_tokens", out var cToken) && cToken.ValueKind == JsonValueKind.Number)
                    outputTokens = cToken.GetInt32();
                
                if(inputTokens.HasValue || outputTokens.HasValue) {
                    _logger?.LogDebug("Parsed DeepSeek token usage: Input={Input}, Output={Output}", inputTokens, outputTokens);
                }
            }
        }
        
        if (containsToolCall && finishReason != "tool_calls") {
            finishReason = "tool_calls";
            _logger?.LogInformation("Forcing finishReason to 'tool_calls' due to presence of tool call in response");
        }
        
        _logger?.LogInformation("[DeepSeekSummary] Processed chunk: TextDelta={TextDelta}, ThinkingDelta={ThinkingDelta}, ToolCallInfo={ToolCallInfo}, FinishReason={FinishReason}", 
            text != null ? $"Length: {text.Length}" : "null",
            thinking != null ? $"Length: {thinking.Length}" : "null",
            toolCallInfo != null ? $"Name: {toolCallInfo.Name}" : "null",
            finishReason);

        return new ParsedChunkInfo(
            TextDelta: text,
            ThinkingDelta: thinking,
            ToolCallInfo: toolCallInfo,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            FinishReason: finishReason
        );
    }

    private ParsedChunkInfo ParseAimlApiChunk(string rawContent)
    {
        _logger?.LogInformation("[StreamDebug] Parsing AimlFlux chunk. RawContent Length: {Length}", rawContent?.Length ?? 0);
        var result = new ParsedChunkInfo(TextDelta: rawContent);
        _logger?.LogInformation("[StreamDebug] Parsed AimlFlux chunk result. TextDelta Length: {Length}", result.TextDelta?.Length ?? 0);
        return result;
    }

    private Guid? FindPluginIdByName(string toolName)
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

    private MessageDto FormatToolResultMessage(ModelType modelType, string toolCallId, string toolName, PluginResult pluginResult, Message originalAiMessage)
    {
        _logger.LogInformation("Formatting tool result for ToolCallId {ToolCallId}, ToolName {ToolName}, Success: {Success}", toolCallId, toolName, pluginResult.Success);
        string resultString = pluginResult.Success ? pluginResult.Result : $"Error: {pluginResult.ErrorMessage}";

        object messagePayload;
        bool isAiMessageRole = false;

        switch (modelType)
        {
            case ModelType.OpenAi:
                messagePayload = new {
                    role = "tool",
                    tool_call_id = toolCallId,
                    content = resultString
                };
                 isAiMessageRole = false; 
                break;

            case ModelType.Anthropic:
                 messagePayload = new {
                    role = "user",
                    content = new [] {
                        new {
                            type = "tool_result",
                            tool_use_id = toolCallId,
                            content = resultString,
                            is_error = !pluginResult.Success
                        }
                    }
                 };
                 isAiMessageRole = false;
                 break;

            case ModelType.Gemini:
                messagePayload = new {
                    parts = new [] {
                        new {
                            functionResponse = new {
                                name = toolName,
                                response = new {
                                    content = TryParseJsonElement(resultString) ?? (object)resultString
                                }
                            }
                        }
                    }
                };
                isAiMessageRole = false;
                break;

            default:
                _logger.LogError("Cannot format tool result message for unsupported provider: {ModelType}", modelType);
                messagePayload = $"[Tool Result ({toolName}) for ID {toolCallId}]: {resultString}";
                isAiMessageRole = false;
                break;
        }

        string contentJson = JsonSerializer.Serialize(messagePayload, new JsonSerializerOptions { WriteIndented = false });

        return new MessageDto(contentJson, isAiMessageRole, Guid.NewGuid());
    }
     
    private async Task FinalizeTokenUsage(
        Guid chatSessionId, 
        AiModel aiModel, 
        int finalInputTokens, 
        int finalOutputTokens, 
        CancellationToken cancellationToken)
    {
         if (finalInputTokens > 0 || finalOutputTokens > 0)
         { 
               decimal finalCost = aiModel.CalculateCost(finalInputTokens, finalOutputTokens);
               _logger.LogInformation("Finalizing token usage for ChatSession {ChatSessionId}: Input={InputTokens}, Output={OutputTokens}, Cost={Cost}", 
                    chatSessionId, finalInputTokens, finalOutputTokens, finalCost);
               await _tokenUsageService.SetTokenUsageFromModelAsync(chatSessionId, finalInputTokens, finalOutputTokens, finalCost, cancellationToken);
         }
         else
         { 
             _logger.LogWarning("Final token counts not reported by provider for ChatSession {ChatSessionId}. Token usage not updated.", chatSessionId);
         }
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
}