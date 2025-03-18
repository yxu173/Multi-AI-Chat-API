using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.ChatFolders.GetChatFolderById;

public sealed class GetChatFolderByIdQueryHandler : IQueryHandler<GetChatFolderByIdQuery, ChatFolderDto>
{
    private readonly IChatFolderRepository _chatFolderRepository;

    public GetChatFolderByIdQueryHandler(IChatFolderRepository chatFolderRepository)
    {
        _chatFolderRepository = chatFolderRepository;
    }

    public async Task<Result<ChatFolderDto>> Handle(GetChatFolderByIdQuery request, CancellationToken cancellationToken)
    {
        var chatFolder = await _chatFolderRepository.GetByIdAsync(request.Id, cancellationToken);
        var result = new ChatFolderDto(chatFolder.Id,
            chatFolder.Name,
            chatFolder.Description,
            chatFolder.CreatedAt,
            chatFolder.ChatSessions.Select(s => new ChatDto(
                s.Id,
                s.Title,
                s.CreatedAt
            )).ToList());

        return Result.Success(result);
    }
}