using System.Linq.Expressions;
using Domain.Aggregates.Chats;
using SharedKernal;

namespace Domain.Repositories;

public interface IChatSessionRepository
{
    Task<ChatSession> GetByIdAsync(Guid id);
    Task<ChatSession> GetByIdWithMessagesAndModelAndProviderAsync(Guid id);

    Task<ChatSession> GetChatWithModel(Guid chatId);
    Task AddAsync(ChatSession chatSession, CancellationToken cancellationToken);
    Task UpdateAsync(ChatSession chatSession, CancellationToken cancellationToken);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatSession>> GetAllChatsByUserId(Guid userId);
    Task<IReadOnlyList<ChatSession>> GetChatSearch(Guid userId, string? searchTerm, bool includeMessages = false);
    Task<int> BulkDeleteAsync(Guid userId, IEnumerable<Guid> chatIds, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<ChatSession> Chats, int TotalCount)> GetRootChatsByUserIdAsync(Guid userId, int page, int pageSize, bool includeCount = true);
    Task<IReadOnlyList<ChatSession>> GetRootChatsByUserIdWithoutCountAsync(Guid userId, int page, int pageSize);
    Task<int> GetRootChatsCountByUserIdAsync(Guid userId);
}