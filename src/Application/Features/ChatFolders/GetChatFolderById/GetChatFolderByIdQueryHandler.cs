using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.ChatFolders.GetChatFolderById;

public sealed class GetChatFolderByIdQueryHandler : IQueryHandler<GetChatFolderByIdQuery, ChatFolderDto>
{
    private readonly IChatFolderRepository _chatFolderRepository;

    public GetChatFolderByIdQueryHandler(IChatFolderRepository chatFolderRepository)
    {
        _chatFolderRepository = chatFolderRepository;
    }

    public async Task<Result<ChatFolderDto>> ExecuteAsync(GetChatFolderByIdQuery command, CancellationToken ct)
    {
        var chatFolder = await _chatFolderRepository.GetByIdAsync(command.Id, ct);
        var result = new ChatFolderDto(chatFolder.Id,
            chatFolder.Name,
            chatFolder.CreatedAt,
            chatFolder.ChatSessions.Select(s => new ChatDto(
                s.Id,
                s.Title,
                s.CreatedAt
            )).ToList());

        return Result.Success(result);
    }
}