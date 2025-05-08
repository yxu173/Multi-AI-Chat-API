using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.ChatFolders.UpdateChatFolder;

public sealed class UpdateChatFolderCommandHandler : ICommandHandler<UpdateChatFolderCommand, bool>
{
    private readonly IChatFolderRepository _chatFolderRepository;

    public UpdateChatFolderCommandHandler(IChatFolderRepository chatFolderRepository)
    {
        _chatFolderRepository = chatFolderRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(UpdateChatFolderCommand command, CancellationToken ct)
    {
        var chatFolder = await _chatFolderRepository.GetByIdAsync(command.Id, ct);
        chatFolder.UpdateDetails(command.Name, command.Description);
        await _chatFolderRepository.UpdateAsync(chatFolder, ct);
        return Result.Success(true);
    }
}