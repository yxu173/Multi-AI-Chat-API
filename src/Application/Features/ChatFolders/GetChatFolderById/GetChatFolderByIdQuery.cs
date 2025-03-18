using Application.Abstractions.Messaging;

namespace Application.Features.ChatFolders.GetChatFolderById;

public sealed record GetChatFolderByIdQuery(Guid Id) : IQuery<ChatFolderDto>;