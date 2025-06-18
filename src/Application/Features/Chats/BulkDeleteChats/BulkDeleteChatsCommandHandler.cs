using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.Chats.BulkDeleteChats;

public class BulkDeleteChatsCommandHandler : ICommandHandler<BulkDeleteChatsCommand, bool>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public BulkDeleteChatsCommandHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(BulkDeleteChatsCommand command, CancellationToken ct)
    {
        var deletedCount = await _chatSessionRepository.BulkDeleteAsync(command.UserId, command.ChatIds, ct);

        if (deletedCount == 0)
        {
            return Result.Failure<bool>(Error.NotFound("Chats.NotFound", "No chats found to delete"));
        }

        return Result.Success(true);
    }
} 