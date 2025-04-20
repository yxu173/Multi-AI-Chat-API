using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Chats.GetAllChatsByUserId;

public class GetAllChatsByUserIdQueryHandler : IQueryHandler<GetAllChatsByUserIdQuery, IReadOnlyList<GetAllChatsDto>>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public GetAllChatsByUserIdQueryHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<IReadOnlyList<GetAllChatsDto>>> Handle(GetAllChatsByUserIdQuery request,
        CancellationToken cancellationToken)
    {
        var chats = await _chatSessionRepository.GetAllChatsByUserId(request.UserId);
        var chatDtos = chats.Select(c => new GetAllChatsDto(c.Id, c.Title)).ToList();
        return Result.Success<IReadOnlyList<GetAllChatsDto>>(chatDtos);
    }
}