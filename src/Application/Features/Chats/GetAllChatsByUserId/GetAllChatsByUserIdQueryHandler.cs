using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.Chats.GetAllChatsByUserId;

public class GetAllChatsByUserIdQueryHandler : IQueryHandler<GetAllChatsByUserIdQuery, IReadOnlyList<GetAllChatsDto>>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public GetAllChatsByUserIdQueryHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<IReadOnlyList<GetAllChatsDto>>> ExecuteAsync(GetAllChatsByUserIdQuery command, CancellationToken ct)
    {
        var chats = await _chatSessionRepository.GetAllChatsByUserId(command.UserId);
        var chatDtos = chats.Select(c => new GetAllChatsDto(c.Id, c.Title)).ToList();
        return Result.Success<IReadOnlyList<GetAllChatsDto>>(chatDtos);
    }

}