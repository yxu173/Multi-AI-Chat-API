using Application.Abstractions.Messaging;
using Domain.Enums;

namespace Application.Features.Chats.CreateChatSession;

public record CreateChatSessionCommand(
    Guid UserId,
    Guid ModelId,
    Guid? FolderId = null,
    string? customApiKey = null
)
    : ICommand<Guid>;