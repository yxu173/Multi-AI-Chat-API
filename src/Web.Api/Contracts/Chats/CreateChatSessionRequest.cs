using Domain.Enums;

namespace Web.Api.Contracts.Chats;

public sealed record CreateChatSessionRequest(Guid ModelId, Guid? FolderId = null);