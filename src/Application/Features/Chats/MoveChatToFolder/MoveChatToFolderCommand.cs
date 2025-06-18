using Application.Abstractions.Messaging;

namespace Application.Features.Chats.MoveChatToFolder;

public sealed record MoveChatToFolderCommand(Guid ChatId, Guid FolderId) : ICommand<bool>;