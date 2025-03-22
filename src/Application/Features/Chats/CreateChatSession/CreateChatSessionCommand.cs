using Application.Abstractions.Messaging;
using Domain.Enums;

namespace Application.Features.Chats.CreateChatSession;

public record CreateChatSessionCommand(
    Guid UserId,
    Guid? ModelId,
    Guid? AiAgentId = null,
    Guid? FolderId = null,
    string? CustomApiKey = null
) : ICommand<Guid>;