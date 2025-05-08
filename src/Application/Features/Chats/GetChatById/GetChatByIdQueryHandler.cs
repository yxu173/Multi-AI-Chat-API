using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Chats.GetChatById;

public class GetChatByIdQueryHandler : IQueryHandler<GetChatByIdQuery, ChatDto>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public GetChatByIdQueryHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<ChatDto>> ExecuteAsync(GetChatByIdQuery command, CancellationToken ct)
    {
        var chatSession = await _chatSessionRepository.GetByIdAsync(
            command.ChatId
        );

        var response = new ChatDto(
            chatSession.Id,
            chatSession.Title,
            chatSession.CreatedAt,
            chatSession.Messages.OrderBy(m => m.CreatedAt)
                .Select(m => new MessageDto
                (
                    m.Id,
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