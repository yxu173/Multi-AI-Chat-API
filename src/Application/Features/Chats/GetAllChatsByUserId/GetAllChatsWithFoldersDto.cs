namespace Application.Features.Chats.GetAllChatsByUserId;

using Application.Features.ChatFolders.GetChatFolderById;

public sealed record GetAllChatsWithFoldersDto(
    IReadOnlyList<ChatFolderDto> Folders,
    IReadOnlyList<ChatDto> RootChats,
    int RootChatsTotalCount
); 