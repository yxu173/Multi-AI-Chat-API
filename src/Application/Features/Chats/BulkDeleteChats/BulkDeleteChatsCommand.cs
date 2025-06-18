using Application.Abstractions.Messaging;

namespace Application.Features.Chats.BulkDeleteChats;

public sealed record BulkDeleteChatsCommand(Guid UserId, IEnumerable<Guid> ChatIds) : ICommand<bool>; 