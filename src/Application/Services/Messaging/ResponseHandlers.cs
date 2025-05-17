using Application.Abstractions.Interfaces; 
using Application.Services.AI; 
using Domain.Aggregates.Chats; 
using Domain.Enums;
using Application.Services.Utilities;

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
        CancellationToken cancellationToken,
        Guid? providerApiKeyId = null);
}
