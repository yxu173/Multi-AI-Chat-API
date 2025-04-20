using Application.Abstractions.Messaging;

namespace Application.Features.Chats.UpdateChatSession;

public sealed record UpdateChatSessionCommand(
    Guid ChatSessionId,
    string Title,
    Guid? FolderId = null,
    bool? EnableThinking = null
) : ICommand<bool>; 