using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.ChatFolders.CreateChatFolder;

public sealed class CreateChatFolderCommandHandler : ICommandHandler<CreateChatFolderCommand, Guid>
{
    private readonly IChatFolderRepository _chatFolderRepository;

    public CreateChatFolderCommandHandler(IChatFolderRepository chatFolderRepository)
    {
        _chatFolderRepository = chatFolderRepository;
    }

    public async Task<Result<Guid>> ExecuteAsync(CreateChatFolderCommand command, CancellationToken ct)
    {
        var chatFolder = ChatFolder.Create(command.UserId, command.Name, command.Description);
        await _chatFolderRepository.AddAsync(chatFolder, ct);
        return Result.Success(chatFolder.Id);
    }
}