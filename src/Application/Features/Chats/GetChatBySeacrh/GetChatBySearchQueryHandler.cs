using Application.Abstractions.Messaging;
using Application.Features.Chats.GetAllChatsByUserId;
using Application.Features.Chats.GetChatById;
using Domain.Repositories;
using MediatR;
using SharedKernel;

namespace Application.Features.Chats.GetChatBySeacrh;

public sealed class GetChatBySearchQueryHandler : IQueryHandler<GetChatBySearchQuery, IEnumerable<GetAllChatsDto>>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public GetChatBySearchQueryHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<IEnumerable<GetAllChatsDto>>> Handle(GetChatBySearchQuery request,
        CancellationToken cancellationToken)
    {
        var chatSessions = await _chatSessionRepository.GetChatSearch(request.UserId, request.Search);

        return Result.Success(chatSessions.Select(c => new GetAllChatsDto(c.Id, c.Title)));
    }
}