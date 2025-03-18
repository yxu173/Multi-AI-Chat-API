using Application.Abstractions.Messaging;

namespace Application.Features.ChatFolders.DeleteChatFolder;

public sealed record DeleteChatFolderCommand(Guid Id) : ICommand<bool>;