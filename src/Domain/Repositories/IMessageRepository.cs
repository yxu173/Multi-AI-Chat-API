using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IMessageRepository
{
    Task<Message?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Message?> GetByIdWithFileAttachmentsAsync(Guid id, CancellationToken cancellationToken);
    Task<Message?> GetLatestAiMessageForChatAsync(Guid chatSessionId, CancellationToken? cancellationToken = null);
    Task AddAsync(Message message, CancellationToken cancellationToken);
    Task UpdateAsync(Message message, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}