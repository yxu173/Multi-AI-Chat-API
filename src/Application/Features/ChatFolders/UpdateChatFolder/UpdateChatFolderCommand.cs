using Application.Abstractions.Messaging;

namespace Application.Features.ChatFolders.UpdateChatFolder;

public sealed record UpdateChatFolderCommand(Guid Id, string Name) : ICommand<bool>;