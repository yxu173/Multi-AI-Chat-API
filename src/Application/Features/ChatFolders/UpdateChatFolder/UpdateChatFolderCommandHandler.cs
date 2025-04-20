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

    public async Task<Result<bool>> Handle(UpdateChatFolderCommand request, CancellationToken cancellationToken)
    {
        var chatFolder = await _chatFolderRepository.GetByIdAsync(request.Id, cancellationToken);
        chatFolder.UpdateDetails(request.Name, request.Description);
        await _chatFolderRepository.UpdateAsync(chatFolder, cancellationToken);
        return Result.Success(true);
    }
}