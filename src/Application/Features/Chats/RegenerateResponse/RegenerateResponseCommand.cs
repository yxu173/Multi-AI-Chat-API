using Application.Abstractions.Messaging;

namespace Application.Features.Chats.RegenerateResponse;

public record RegenerateResponseCommand(
    Guid ChatSessionId,
    Guid UserId,
    Guid UserMessageId
) : ICommand; 