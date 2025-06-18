namespace Web.Api.Contracts.Chats;

public sealed record ShareChatRequest(
    DateTime? ExpiresAt = null
); 