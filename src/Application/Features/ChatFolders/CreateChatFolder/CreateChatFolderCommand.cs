using Application.Abstractions.Messaging;

namespace Application.Features.ChatFolders.CreateChatFolder;

public sealed record CreateChatFolderCommand(Guid UserId, string Name) : ICommand<Guid>;