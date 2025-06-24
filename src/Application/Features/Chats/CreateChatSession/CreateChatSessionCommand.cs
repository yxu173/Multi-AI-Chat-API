using Application.Abstractions.Messaging;
using Domain.Enums;

namespace Application.Features.Chats.CreateChatSession;

public record CreateChatSessionCommand(
    string ChatType,
    Guid UserId,
    Guid? ModelId,
    Guid? FolderId = null,
    Guid? AiAgentId = null,
    string? CustomApiKey = null,
    bool EnableThinking = false
) : ICommand<Guid>;