using Application.Abstractions.Messaging;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.ChatFolders.CreateChatFolder;

public sealed class CreateChatFolderCommandHandler : ICommandHandler<CreateChatFolderCommand, Guid>
{
    private readonly IChatFolderRepository _chatFolderRepository;

    public CreateChatFolderCommandHandler(IChatFolderRepository chatFolderRepository)
    {
        _chatFolderRepository = chatFolderRepository;
    }

    public async Task<Result<Guid>> Handle(CreateChatFolderCommand request, CancellationToken cancellationToken)
    {
        var chatFolder = ChatFolder.Create(request.UserId, request.Name, request.Description);
        await _chatFolderRepository.AddAsync(chatFolder, cancellationToken);
        return Result.Success(chatFolder.Id);
    }
}