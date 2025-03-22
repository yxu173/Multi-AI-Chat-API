using Domain.Aggregates.Chats;
using SharedKernel;

namespace Domain.Repositories;

public interface IChatSessionRepository
{
    Task<ChatSession> GetByIdAsync(Guid id);
    Task<ChatSession> GetByIdWithModelAsync(Guid id);
    Task AddAsync(ChatSession chatSession, CancellationToken cancellationToken);
    Task UpdateAsync(ChatSession chatSession, CancellationToken cancellationToken);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatSession>> GetAllChatsByUserId(Guid userId);
    Task<IReadOnlyList<ChatSession>> GetChatSearch(Guid userId, string? searchTerm, bool includeMessages = false);
}