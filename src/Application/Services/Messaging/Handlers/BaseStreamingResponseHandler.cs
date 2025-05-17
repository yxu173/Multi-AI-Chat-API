using System.Text;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Application.Services.AI.Streaming;
using Domain.Aggregates.Chats;
using Domain.Enums;
using System.Collections.Generic;
using Application.Services.AI.Interfaces;
using Application.Services.Utilities;

namespace Application.Services.Messaging.Handlers;

public abstract class BaseStreamingResponseHandler : IResponseHandler
{
    private readonly IAiRequestHandler _aiRequestHandler;
    private readonly StreamProcessor _streamProcessor;
    private readonly ToolCallHandler _toolCallHandler;

    protected BaseStreamingResponseHandler(
        IAiRequestHandler aiRequestHandler,
        StreamProcessor streamProcessor,
        ToolCallHandler toolCallHandler)
    {
        _aiRequestHandler = aiRequestHandler;
        _streamProcessor = streamProcessor;
        _toolCallHandler = toolCallHandler;
    }

    public abstract ResponseType ResponseType { get; }

    protected virtual bool AllowToolCalls => ResponseType == ResponseType.ToolCall;

    public async Task<ResponseHandlerResult> HandleAsync(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        ModelType modelType,
        CancellationToken cancellationToken,
        Guid? providerApiKeyId = null)
    {
        var finalContent = new StringBuilder();
        var toolResults = new List<MessageDto>();
        MessageDto? toolRequestMsg = null;
        string? thinkingContent = null;
        int inTokens = 0;
        int outTokens = 0;
        bool completed = false;
        int turn = 0;
        const int MaxTurns = 5;

        var baseHistory = new List<MessageDto>(requestContext.History);

        while (turn < MaxTurns && !completed && !cancellationToken.IsCancellationRequested)
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

            var rawStream = aiService.StreamResponseAsync(payload, cancellationToken, providerApiKeyId);
            toolResults.Clear();
            toolRequestMsg = null;

            var streamResult = await _streamProcessor.ProcessStreamAsync(
                rawStream,
                modelType,
                requestContext.SpecificModel.SupportsThinking || requestContext.ChatSession.EnableThinking,
                aiMessage,
                requestContext.ChatSession.Id,
                txt => finalContent.Append(txt),
                cancellationToken);

            if (!string.IsNullOrEmpty(streamResult.ThinkingContent)) thinkingContent = streamResult.ThinkingContent;
            inTokens += streamResult.InputTokens;
            outTokens += streamResult.OutputTokens;

            if (cancellationToken.IsCancellationRequested) break;

            if (AllowToolCalls && streamResult.ToolCalls?.Any() == true)
            {
                aiMessage.UpdateContent(finalContent.ToString());
                foreach (var call in streamResult.ToolCalls)
                {
                    var resultMsg = await _toolCallHandler.ExecuteToolCallAsync(call, modelType, aiMessage, cancellationToken);
                    toolResults.Add(resultMsg);
                }
                toolRequestMsg = await _toolCallHandler.FormatAiMessageWithToolCallsAsync(modelType, streamResult.ToolCalls);
                finalContent.Clear();
            }
            else if (streamResult.IsComplete)
            {
                completed = true;
                aiMessage.UpdateContent(finalContent.ToString());
            }
        }

        if (!completed && !cancellationToken.IsCancellationRequested)
        {
            aiMessage.UpdateContent(finalContent.ToString());
            aiMessage.InterruptMessage();
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            aiMessage.InterruptMessage();
        }

        return new ResponseHandlerResult(inTokens, outTokens, completed, thinkingContent);
    }
}
