using Domain.Aggregates.Chats;
using SharedKernal;

namespace Domain.Repositories;

public interface ISharedChatRepository
{
    Task<SharedChat?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<SharedChat?> GetByChatIdAsync(Guid chatId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SharedChat>> GetSharedByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(SharedChat sharedChat, CancellationToken cancellationToken = default);
    Task UpdateAsync(SharedChat sharedChat, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
} 