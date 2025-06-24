using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernal;
using Application.Features.ChatFolders.GetChatFolderById;

namespace Application.Features.Chats.GetAllChatsByUserId;

public class GetAllChatsByUserIdQueryHandler : IQueryHandler<GetAllChatsByUserIdQuery, GetAllChatsWithFoldersDto>
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IChatFolderRepository _chatFolderRepository;

    public GetAllChatsByUserIdQueryHandler(IChatSessionRepository chatSessionRepository, IChatFolderRepository chatFolderRepository)
    {
        _chatSessionRepository = chatSessionRepository;
        _chatFolderRepository = chatFolderRepository;
    }

    public async Task<Result<GetAllChatsWithFoldersDto>> ExecuteAsync(GetAllChatsByUserIdQuery command, CancellationToken ct)
    {
        int page = command.Page > 0 ? command.Page : 1;
        int pageSize = command.PageSize > 0 ? command.PageSize : 20;

        var (rootChats, rootTotal) = await _chatSessionRepository.GetRootChatsByUserIdAsync(command.UserId, page, pageSize);
        var rootChatDtos = rootChats.Select(c => new ChatDto(c.Id, c.Title, c.CreatedAt)).ToList();

        var chatFolders = await _chatFolderRepository.GetByUserIdAsync(command.UserId, ct);
        var folderDtos = chatFolders.Select(
            f => new ChatFolderDto(
                f.Id,
                f.Name,
                f.CreatedAt,
                f.ChatSessions.Select(s => new ChatDto(s.Id, s.Title, s.CreatedAt)).ToList()
            )).ToList();

        var result = new GetAllChatsWithFoldersDto(folderDtos, rootChatDtos, rootTotal);
        return Result.Success(result);
    }
}