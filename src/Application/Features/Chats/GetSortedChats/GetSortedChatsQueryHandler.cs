using Application.Abstractions.Messaging;
using Application.Features.Chats.GetAllChatsByUserId;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.Chats.GetSortedChats;

public class GetSortedChatsQueryHandler : IQueryHandler<GetSortedChatsQuery, IReadOnlyList<GetAllChatsDto>>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public GetSortedChatsQueryHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<IReadOnlyList<GetAllChatsDto>>> ExecuteAsync(GetSortedChatsQuery query, CancellationToken ct)
    {
        var chats = await _chatSessionRepository.GetAllChatsByUserId(query.UserId);
        
        var sortedChats = query.SortOrder switch
        {
            ChatSortOrder.OldestFirst => chats.OrderBy(c => c.CreatedAt),
            ChatSortOrder.TitleAZ => chats.OrderBy(c => c.Title),
            ChatSortOrder.TitleZA => chats.OrderByDescending(c => c.Title),
            _ => chats.OrderByDescending(c => c.CreatedAt)
        };

        var chatDtos = sortedChats.Select(c => new GetAllChatsDto(c.Id, c.Title)).ToList();
        return Result.Success<IReadOnlyList<GetAllChatsDto>>(chatDtos);
    }
} 