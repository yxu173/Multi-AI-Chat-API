using Application.Abstractions.Messaging;
using Application.Features.ChatFolders.GetChatFolderById;

namespace Application.Features.ChatFolders.GetChatsFolderByUserId;

public sealed record GetChatsFolderByUserIdQuery(Guid UserId) : IQuery<IReadOnlyList<ChatFolderDto>>;