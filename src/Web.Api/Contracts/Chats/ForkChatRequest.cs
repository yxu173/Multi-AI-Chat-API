namespace Web.Api.Contracts.Chats;

public record ForkChatRequest(
    Guid OriginalChatId,
    Guid ForkFromMessageId,
    Guid NewAiModelId);