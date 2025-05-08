using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.ChatFolders.DeleteChatFolder;

public sealed class DeleteChatFolderCommandHandler : ICommandHandler<DeleteChatFolderCommand, bool>
{
    private readonly IChatFolderRepository _chatFolderRepository;

    public DeleteChatFolderCommandHandler(IChatFolderRepository chatFolderRepository)
    {
        _chatFolderRepository = chatFolderRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(DeleteChatFolderCommand command, CancellationToken ct)
    {
        await _chatFolderRepository.DeleteAsync(command.Id, ct);
        return Result.Success(true);
    }
}