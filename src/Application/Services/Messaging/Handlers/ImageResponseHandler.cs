using Application.Abstractions.Interfaces;
using Application.Notifications;
using Application.Services.AI;
using Application.Services.AI.Interfaces;
using Application.Services.Utilities;
using Domain.Aggregates.Chats;
using Domain.Enums;
using FastEndpoints;

namespace Application.Services.Messaging.Handlers;

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
