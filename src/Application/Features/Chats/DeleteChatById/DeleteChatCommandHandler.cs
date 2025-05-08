using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Chats.DeleteChatById;

public sealed class DeleteChatCommandHandler : ICommandHandler<DeleteChatCommand, bool>
{
    private readonly IChatSessionRepository _chatSessionRepository;

    public DeleteChatCommandHandler(IChatSessionRepository chatSessionRepository)
    {
        _chatSessionRepository = chatSessionRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(DeleteChatCommand command, CancellationToken ct)
    {
        var result = await _chatSessionRepository.DeleteAsync(command.Id, ct);
        if (result.IsFailure)
        {
            return Result.Failure<bool>(result.Error);
        }
        return Result.Success(true);
    }
}