namespace Web.Api.Contracts.Chats;

public sealed record BulkDeleteChatsRequest(IEnumerable<Guid> ChatIds); 