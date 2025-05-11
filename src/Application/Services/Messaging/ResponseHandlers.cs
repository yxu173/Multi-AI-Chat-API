using System.Text;
using Application.Abstractions.Interfaces;
using Application.Notifications;
using Application.Services.AI;
using Application.Services.AI.Streaming;
using Application.Services.Utilities;
using Domain.Aggregates.Chats;
using Domain.Enums;
using FastEndpoints;

namespace Application.Services.Messaging;

public record ResponseHandlerResult(
    int TotalInputTokens,
    int TotalOutputTokens,
    bool AiResponseCompleted,
    string? AccumulatedThinkingContent);

public interface IResponseHandler
{
    ResponseType ResponseType { get; }

    Task<ResponseHandlerResult> HandleAsync(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        ModelType modelType,
        CancellationToken cancellationToken);
}

#region Image Handler
public sealed class ImageResponseHandler : IResponseHandler
{
    private readonly IAiRequestHandler _aiRequestHandler;

    public ImageResponseHandler(IAiRequestHandler aiRequestHandler)
    {
        _aiRequestHandler = aiRequestHandler;
    }

    public ResponseType ResponseType => ResponseType.Image;

    public async Task<ResponseHandlerResult> HandleAsync(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        ModelType modelType,
        CancellationToken cancellationToken)
    {
        var payload = await _aiRequestHandler.PrepareRequestPayloadAsync(requestContext, cancellationToken);
        string? markdown = null;
        bool completed = false;

        await foreach (var chunk in aiService.StreamResponseAsync(payload, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (!string.IsNullOrEmpty(chunk.RawContent)) markdown = chunk.RawContent;
            if (chunk.IsCompletion)
            {
                completed = !string.IsNullOrEmpty(markdown);
                break;
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            aiMessage.AppendContent("\n[Cancelled]");
            aiMessage.InterruptMessage();
        }
        else if (completed && markdown != null)
        {
            aiMessage.UpdateContent(markdown);
            aiMessage.CompleteMessage();
            await new MessageChunkReceivedNotification(requestContext.ChatSession.Id, aiMessage.Id, markdown)
                .PublishAsync(cancellation: cancellationToken);
        }
        else
        {
            aiMessage.AppendContent($"\n[Failed to get valid image response from {modelType}]");
            aiMessage.FailMessage();
        }

        return new ResponseHandlerResult(0, 0, completed, null);
    }
}
#endregion

#region Base Streaming Handler
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
        CancellationToken cancellationToken)
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

            var rawStream = aiService.StreamResponseAsync(payload, cancellationToken);
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
#endregion

#region Concrete Streaming Handlers
public sealed class ToolCallStreamingResponseHandler : BaseStreamingResponseHandler
{
    public ToolCallStreamingResponseHandler(
        IAiRequestHandler aiRequestHandler,
        StreamProcessor streamProcessor,
        ToolCallHandler toolCallHandler)
        : base(aiRequestHandler, streamProcessor, toolCallHandler) { }

    public override ResponseType ResponseType => ResponseType.ToolCall;
}

public sealed class TextStreamingResponseHandler : BaseStreamingResponseHandler
{
    public TextStreamingResponseHandler(
        IAiRequestHandler aiRequestHandler,
        StreamProcessor streamProcessor,
        ToolCallHandler toolCallHandler)
        : base(aiRequestHandler, streamProcessor, toolCallHandler) { }

    public override ResponseType ResponseType => ResponseType.Text;

    protected override bool AllowToolCalls => false;
}
#endregion
