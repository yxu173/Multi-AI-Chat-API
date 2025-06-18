using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.Chats.MoveChatToFolder;

public sealed class MoveChatToFolderCommandHandler : ICommandHandler<MoveChatToFolderCommand, bool>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public MoveChatToFolderCommandHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(MoveChatToFolderCommand command, CancellationToken ct)
    {
        if (command.ChatId == Guid.Empty)
        {
            return Result.Failure<bool>(Error.Validation("Chat ID cannot be empty.",
                "The provided Chat ID is invalid."));
        }

        var chat = await _chatSessionRepository.GetByIdAsync(command.ChatId);
        chat.MoveToFolder(command.FolderId);

        await _chatSessionRepository.UpdateAsync(chat, ct);
        return Result.Success(true);
    }
}