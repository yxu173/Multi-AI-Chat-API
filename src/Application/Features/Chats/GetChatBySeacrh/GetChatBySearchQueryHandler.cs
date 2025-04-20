using Application.Abstractions.Messaging;
using Application.Features.Chats.GetAllChatsByUserId;
using Application.Features.Chats.GetChatById;
using Domain.Repositories;
using MediatR;
using SharedKernel;

namespace Application.Features.Chats.GetChatBySeacrh;

public sealed class GetChatBySearchQueryHandler : IQueryHandler<GetChatBySearchQuery, IEnumerable<ChatSearchResultDto>>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public GetChatBySearchQueryHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<IEnumerable<ChatSearchResultDto>>> Handle(GetChatBySearchQuery request,
        CancellationToken cancellationToken)
    {
        var chatSessions = await _chatSessionRepository.GetChatSearch(
            request.UserId, 
            request.Search,
            includeMessages: true);

        var results = chatSessions.Select(c => new ChatSearchResultDto(
            c.Id,
            c.Title,
            c.Messages
                .Where(m => string.IsNullOrEmpty(request.Search) || 
                           m.Content.Contains(request.Search))
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