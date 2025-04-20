using Domain.Enums;

namespace Web.Api.Contracts.Chats;

public sealed record CreateChatSessionRequest(
    Guid? ModelId, 
    Guid? FolderId = null, 
    Guid? AiAgentId = null, 
    string? CustomApiKey = null,
    bool EnableThinking = false
);