using Application.Abstractions.Messaging;
using Application.Features.Chats.GetChatById;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.Chats.GetSharedChat;

public class GetSharedChatQueryHandler : IQueryHandler<GetSharedChatQuery, ChatDto>
{
    private readonly ISharedChatRepository _sharedChatRepository;

    public GetSharedChatQueryHandler(ISharedChatRepository sharedChatRepository)
    {
        _sharedChatRepository = sharedChatRepository;
    }

    public async Task<Result<ChatDto>> ExecuteAsync(GetSharedChatQuery query, CancellationToken ct)
    {
        var sharedChat = await _sharedChatRepository.GetByTokenAsync(query.ShareToken, ct);
        if (sharedChat == null)
        {
            return Result.Failure<ChatDto>(Error.NotFound("SharedChat.NotFound", "Shared chat not found or has expired"));
        }

        var chat = sharedChat.Chat;
        var response = new ChatDto(
            chat.Id,
            chat.Title,
            chat.CreatedAt,
            chat.Messages.OrderBy(m => m.CreatedAt)
                .Select(m => new MessageDto
                (
                    m.Id,
                    m.ThinkingContent,
                    m.Content,
                    m.IsFromAi,
                    m.CreatedAt,
                    m.FileAttachments
                        .Select(f => new FileAttachmentDto
                        (
                            f.Id,
                            f.FileName,
                            f.FilePath
                        ))
                        .ToList()))
                .ToList()
        );

        return Result.Success(response);
    }
} 