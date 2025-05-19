using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.Chats.GetChatBySeacrh;

public sealed class GetChatBySearchQueryHandler : IQueryHandler<GetChatBySearchQuery, IEnumerable<ChatSearchResultDto>>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public GetChatBySearchQueryHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<IEnumerable<ChatSearchResultDto>>> ExecuteAsync(GetChatBySearchQuery command, CancellationToken ct)
    {
        var chatSessions = await _chatSessionRepository.GetChatSearch(
            command.UserId, 
            command.Search,
            includeMessages: true);

        var results = chatSessions.Select(c => new ChatSearchResultDto(
            c.Id,
            c.Title,
            c.Messages
                .Where(m => string.IsNullOrEmpty(command.Search) || 
                           m.Content.Contains(command.Search))
                .Select(m => new MessageSearchResultDto(
                    m.Id,
                    m.Content,
                    m.CreatedAt
                ))
                .ToList()
        ));

        return Result.Success(results.AsEnumerable());
    }
}