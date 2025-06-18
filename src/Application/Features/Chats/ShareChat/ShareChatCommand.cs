using Application.Abstractions.Messaging;

namespace Application.Features.Chats.ShareChat;

public sealed record ShareChatCommand(
    Guid ChatId,
    Guid OwnerId,
    DateTime? ExpiresAt = null
) : ICommand<string>; 