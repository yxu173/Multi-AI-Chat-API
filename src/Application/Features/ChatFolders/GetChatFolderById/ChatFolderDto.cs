namespace Application.Features.ChatFolders.GetChatFolderById;

public sealed record ChatFolderDto(
    Guid Id,
    string Name,
    string Description,
    DateTime CreatedAt,
    IReadOnlyList<ChatDto> Chats);

public sealed record ChatDto(Guid Id, string Title, DateTime CreatedAt);