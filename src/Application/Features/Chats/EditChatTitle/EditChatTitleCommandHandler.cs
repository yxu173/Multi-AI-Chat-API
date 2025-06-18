using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.Chats.EditChatTitle;

public sealed class EditChatTitleCommandHandler : ICommandHandler<EditChatTitleCommand, bool>
{
    private readonly IChatSessionRepository _chatRepository;

    public EditChatTitleCommandHandler(IChatSessionRepository chatRepository)
    {
        _chatRepository = chatRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(EditChatTitleCommand command, CancellationToken ct)
    {
        var chat = await _chatRepository.GetByIdAsync(command.ChatId);
        
        if (chat == null)
        {
            return Result.Failure<bool>(Error.NotFound("Chat not found",
                $"Chat with ID {command.ChatId} does not exist."));
        }
        chat.UpdateTitle(command.Title);
        await _chatRepository.UpdateAsync(chat, ct);
        return Result.Success(true);
    }
}