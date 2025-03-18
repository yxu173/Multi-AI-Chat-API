using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IChatFolderRepository
{
    Task<ChatFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChatFolder>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ChatFolder> AddAsync(ChatFolder folder, CancellationToken cancellationToken = default);
    Task<ChatFolder> UpdateAsync(ChatFolder folder, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
