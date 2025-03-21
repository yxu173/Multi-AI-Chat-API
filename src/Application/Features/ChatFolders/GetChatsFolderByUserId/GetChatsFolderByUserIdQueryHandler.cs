using Application.Abstractions.Messaging;
using Application.Features.ChatFolders.GetChatFolderById;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.ChatFolders.GetChatsFolderByUserId;

public sealed class
    GetChatsFolderByUserIdQueryHandler : IQueryHandler<GetChatsFolderByUserIdQuery, IReadOnlyList<ChatFolderDto>>
{
    private readonly IChatFolderRepository _chatFolderRepository;

    public GetChatsFolderByUserIdQueryHandler(IChatFolderRepository chatFolderRepository)
    {
        _chatFolderRepository = chatFolderRepository;
    }

    public async Task<Result<IReadOnlyList<ChatFolderDto>>> Handle(GetChatsFolderByUserIdQuery request,
        CancellationToken cancellationToken)
    {
        var chatFolders = await _chatFolderRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        var result = chatFolders.Select(
            f => new ChatFolderDto(
                f.Id,
                f.Name,
                f.Description,
                f.CreatedAt,
                f.ChatSessions.Select(s => new ChatDto(
                    s.Id,
                    s.Title,
                    s.CreatedAt
                )).ToList()
            )).ToList();
        return Result.Success<IReadOnlyList<ChatFolderDto>>(result);
    }
}