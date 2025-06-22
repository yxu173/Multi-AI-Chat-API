using Application.Abstractions.Messaging;

namespace Application.Features.Chats.ForkChat;

public record ForkChatCommand(
    Guid UserId,
    Guid OriginalChatSessionId,
    Guid ForkFromMessageId,
    Guid NewAiModelId) : ICommand<ForkChatResponse>; 