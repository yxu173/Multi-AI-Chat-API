using Application.Services.AI;
using Domain.Aggregates.Chats;

namespace Application.Abstractions.Interfaces;

public interface IMessageStreamer
{
    Task StreamResponseAsync(
        AiRequestContext requestContext,
        Message aiMessage,
        IAiModelService aiService,
        CancellationToken cancellationToken,
        Guid? providerApiKeyId = null);
}