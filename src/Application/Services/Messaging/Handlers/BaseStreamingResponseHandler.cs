using System.Text;
using Application.Abstractions.Interfaces;
using Application.Services.AI;
using Application.Services.AI.Streaming;
using Domain.Aggregates.Chats;
using Domain.Enums;
using System.Collections.Generic;
using System.Linq;
using Application.Services.AI.Interfaces;
using Application.Services.Utilities;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using Application.Notifications;

namespace Application.Services.Messaging.Handlers;

public abstract class BaseStreamingResponseHandler : IResponseHandler
{
    private readonly ConversationTurnProcessor _turnProcessor;

    protected BaseStreamingResponseHandler(ConversationTurnProcessor turnProcessor)
    {
        _turnProcessor = turnProcessor;
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
        var result = await _turnProcessor.Process(
            requestContext,
            aiMessage,
            aiService,
            modelType,
            AllowToolCalls,
            cancellationToken,
            providerApiKeyId
        );

        return new ResponseHandlerResult(
            result.InputTokens,
            result.OutputTokens,
            result.ConversationCompleted,
            result.AccumulatedThinkingContent
        );
    }
}
