using System.Text;
using Application.Abstractions.Interfaces;
using Application.Notifications;
using Application.Services.AI.Interfaces;
using Application.Services.Messaging;
using Domain.Aggregates.Chats;
using Domain.Enums;
using FastEndpoints;
using Microsoft.Extensions.Logging;

namespace Application.Services.AI.Streaming;

public record ConversationTurnResult(
    int InputTokens,
    int OutputTokens,
    bool ConversationCompleted,
    string? AccumulatedThinkingContent
);

public class ConversationTurnProcessor
{
    private readonly IAiRequestHandler _aiRequestHandler;
    private readonly ToolCallHandler _toolCallHandler;
    private readonly ILogger<ConversationTurnProcessor> _logger;

    public ConversationTurnProcessor(
        IAiRequestHandler aiRequestHandler,
        ToolCallHandler toolCallHandler,
        ILogger<ConversationTurnProcessor> logger)
    {
        _aiRequestHandler = aiRequestHandler;
        _toolCallHandler = toolCallHandler;
        _logger = logger;
    }

    public async Task<ConversationTurnResult> Process(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        ModelType modelType,
        bool allowToolCalls,
        CancellationToken cancellationToken,
        Guid? providerApiKeyId = null)
    {
        var finalContent = new StringBuilder();
        var toolResults = new List<MessageDto>();
        MessageDto? toolRequestMsg = null;
        string? thinkingContent = null;
        int inTokens = 0;
        int outTokens = 0;
        bool conversationCompleted = false;
        int turn = 0;
        const int MaxTurns = 5;

        var baseHistory = new List<MessageDto>(requestContext.History);

        while (turn < MaxTurns && !conversationCompleted && !cancellationToken.IsCancellationRequested)
        {
            turn++;

            var historyTurn = new List<MessageDto>(baseHistory);
            if (toolRequestMsg != null)
            {
                historyTurn.Add(toolRequestMsg);
                historyTurn.AddRange(toolResults);
            }

            var ctxTurn = requestContext with { History = historyTurn };
            var payload = await _aiRequestHandler.PrepareRequestPayloadAsync(ctxTurn, cancellationToken);
            var stream = aiService.StreamResponseAsync(payload, cancellationToken, providerApiKeyId);

            toolResults.Clear();
            toolRequestMsg = null;
            finalContent.Clear();

            var toolCallStates = new Dictionary<int, ToolCallState>();
            var thinkingContentBuilder = new StringBuilder();
            List<ParsedToolCall>? completedToolCalls = null;
            bool turnCompleted = false;

            await foreach (var chunk in stream.WithCancellation(cancellationToken))
            {
                if (chunk.InputTokens.HasValue) inTokens = chunk.InputTokens.Value;
                if (chunk.OutputTokens.HasValue) outTokens = chunk.OutputTokens.Value;

                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    finalContent.Append(chunk.TextDelta);
                    await new MessageChunkReceivedNotification(requestContext.ChatSession.Id, aiMessage.Id, chunk.TextDelta).PublishAsync(cancellation: cancellationToken);
                }

                bool supportsThinking = requestContext.RequestSpecificThinking ?? requestContext.ChatSession.EnableThinking;
                if (supportsThinking && !string.IsNullOrEmpty(chunk.ThinkingDelta))
                {
                    thinkingContentBuilder.Append(chunk.ThinkingDelta);
                    await new ThinkingUpdateNotification(requestContext.ChatSession.Id, aiMessage.Id, chunk.ThinkingDelta).PublishAsync(cancellation: cancellationToken);
                }

                if (chunk.ToolCallInfo != null)
                {
                    var toolInfo = chunk.ToolCallInfo;
                    if (!toolCallStates.TryGetValue(toolInfo.Index, out var state))
                    {
                        state = new ToolCallState();
                        toolCallStates[toolInfo.Index] = state;
                    }
                    if (toolInfo.Id != null) state.Id = toolInfo.Id;
                    if (toolInfo.Name != null) state.Name = toolInfo.Name;
                    if (toolInfo.ArgumentChunk != null) state.ArgumentBuffer.Append(toolInfo.ArgumentChunk);
                }

                if (!string.IsNullOrEmpty(chunk.FinishReason))
                {
                    _logger.LogInformation("Stream turn {Turn} finished with reason: {FinishReason}", turn, chunk.FinishReason);
                    turnCompleted = true;
                    if (chunk.FinishReason == "tool_calls" || chunk.FinishReason == "function_call")
                    {
                        completedToolCalls = toolCallStates.Values
                            .Where(s => !string.IsNullOrEmpty(s.Name) && s.ArgumentBuffer.Length > 0)
                            .Select(s => new ParsedToolCall(s.Id!, s.Name!, s.ArgumentBuffer.ToString()))
                            .ToList();
                    }
                    else
                    {
                        conversationCompleted = true;
                    }
                    break;
                }
            }

            if (thinkingContentBuilder.Length > 0)
            {
                thinkingContent = thinkingContentBuilder.ToString();
            }

            if (cancellationToken.IsCancellationRequested) break;

            if (allowToolCalls && completedToolCalls?.Any() == true)
            {
                aiMessage.UpdateContent(finalContent.ToString());
                foreach (var call in completedToolCalls)
                {
                    var resultMsg = await _toolCallHandler.ExecuteToolCallAsync(aiService, call, cancellationToken);
                    toolResults.Add(resultMsg);
                }
                toolRequestMsg = await _toolCallHandler.FormatAiMessageWithToolCallsAsync(modelType, completedToolCalls);
            }
            else if (turnCompleted)
            {
                conversationCompleted = true;
                aiMessage.UpdateContent(finalContent.ToString());
            }
        }

        if (!conversationCompleted && !cancellationToken.IsCancellationRequested)
        {
            aiMessage.UpdateContent(finalContent.ToString());
            aiMessage.InterruptMessage();
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            aiMessage.InterruptMessage();
        }

        return new ConversationTurnResult(inTokens, outTokens, conversationCompleted, thinkingContent);
    }

    private class ToolCallState
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder ArgumentBuffer { get; } = new StringBuilder();
    }
} 