using Application.Abstractions.Messaging;
using Application.Features.Chats.GetAllChatsByUserId;

namespace Application.Features.Chats.GetSortedChats;

public enum ChatSortOrder
{
    OldestFirst,
    TitleAZ,
    TitleZA
}

public sealed record GetSortedChatsQuery(Guid UserId, ChatSortOrder SortOrder) : IQuery<IReadOnlyList<GetAllChatsDto>>; 