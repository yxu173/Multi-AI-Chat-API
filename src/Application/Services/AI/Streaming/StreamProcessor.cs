using System.Text;
using Application.Abstractions.Interfaces;
using Application.Notifications;
using Domain.Aggregates.Chats;
using Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public record StreamProcessingResult(
    int InputTokens,
    int OutputTokens,
    List<ParsedToolCall>? ToolCalls,
    bool IsComplete,
    string? ThinkingContent = null);

public record ParsedToolCall(string Id, string Name, string Arguments);

public class StreamProcessor
{
    private readonly ILogger<StreamProcessor> _logger;
    private readonly IMediator _mediator;
    private readonly IEnumerable<IStreamChunkParser> _parsers;

    public StreamProcessor(
        ILogger<StreamProcessor> logger,
        IMediator mediator,
        IEnumerable<IStreamChunkParser> parsers)
    {
        _logger = logger;
        _mediator = mediator;
        _parsers = parsers;
    }

    public async Task<StreamProcessingResult> ProcessStreamAsync(
        IAsyncEnumerable<AiRawStreamChunk> rawStream,
        ModelType modelType,
        bool supportsThinking,
        Message aiMessage,
        Guid chatSessionId,
        Action<string> appendFinalContentAction,
        CancellationToken cancellationToken)
    {
        var parser = _parsers.FirstOrDefault(p => p.SupportedModelType == modelType);
        if (parser == null)
        {
            _logger.LogError("No parser found for model type {ModelType}", modelType);
            return new StreamProcessingResult(0, 0, null, false);
        }

        int inputTokens = 0;
        int outputTokens = 0;
        var toolCallStates = new Dictionary<int, ToolCallState>();
        bool isComplete = false;
        List<ParsedToolCall>? completedToolCalls = null;
        StringBuilder thinkingContentBuilder = new StringBuilder();

        try
        {
            await foreach (var chunk in rawStream.WithCancellation(cancellationToken))
            {
                if (string.IsNullOrEmpty(chunk.RawContent))
                {
                    _logger.LogWarning("Received empty chunk from stream");
                    continue;
                }

                var parsedInfo = parser.ParseChunk(chunk.RawContent);

                // Update token counts if provided
                if (parsedInfo.InputTokens.HasValue) inputTokens = parsedInfo.InputTokens.Value;
                if (parsedInfo.OutputTokens.HasValue) outputTokens = parsedInfo.OutputTokens.Value;

                // Handle text content
                if (!string.IsNullOrEmpty(parsedInfo.TextDelta))
                {
                    appendFinalContentAction(parsedInfo.TextDelta);
                    await _mediator.Publish(
                        new MessageChunkReceivedNotification(chatSessionId, aiMessage.Id, parsedInfo.TextDelta),
                        cancellationToken);
                }

                // Handle thinking indicators
                if (supportsThinking && !string.IsNullOrEmpty(parsedInfo.ThinkingDelta))
                {
                    thinkingContentBuilder.Append(parsedInfo.ThinkingDelta);

                    await _mediator.Publish(
                        new ThinkingUpdateNotification(chatSessionId, aiMessage.Id, parsedInfo.ThinkingDelta),
                        cancellationToken);
                }

                // Handle tool calls
                if (parsedInfo.ToolCallInfo != null)
                {
                    var toolInfo = parsedInfo.ToolCallInfo;
                    if (!toolCallStates.TryGetValue(toolInfo.Index, out var state))
                    {
                        state = new ToolCallState();
                        toolCallStates[toolInfo.Index] = state;
                    }

                    if (toolInfo.Id != null) state.Id = toolInfo.Id;
                    if (toolInfo.Name != null) state.Name = toolInfo.Name;
                    if (toolInfo.ArgumentChunk != null) state.ArgumentBuffer.Append(toolInfo.ArgumentChunk);
                    state.IsComplete = toolInfo.IsComplete;
                }

                // Check for completion
                if (!string.IsNullOrEmpty(parsedInfo.FinishReason))
                {
                    _logger.LogInformation("Stream indicated completion with reason: {FinishReason}",
                        parsedInfo.FinishReason);

                    if (parsedInfo.FinishReason == "tool_calls" || parsedInfo.FinishReason == "function_call")
                    {
                        completedToolCalls = toolCallStates.Values
                            .Where(s => !string.IsNullOrEmpty(s.Name))
                            .Select(s => new ParsedToolCall(s.Id, s.Name, s.ArgumentBuffer.ToString()))
                            .ToList();
                        isComplete = true;
                        break;
                    }
                    else if (parsedInfo.FinishReason == "stop" || parsedInfo.FinishReason == "length")
                    {
                        isComplete = true;
                        aiMessage.CompleteMessage();
                        break;
                    }
                    else if (parsedInfo.FinishReason == "error")
                    {
                        _logger.LogError("Stream ended with error finish reason");
                        aiMessage.FailMessage();
                        isComplete = true;
                        break;
                    }
                }
            }

            if (!isComplete && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Stream ended without completion signal");
            }

            return new StreamProcessingResult(
                inputTokens,
                outputTokens,
                completedToolCalls,
                isComplete,
                thinkingContentBuilder.ToString()
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Stream processing cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing stream");
            throw;
        }
    }
}

internal class ToolCallState
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public StringBuilder ArgumentBuffer { get; } = new StringBuilder();
    public bool IsComplete { get; set; } = false;
}