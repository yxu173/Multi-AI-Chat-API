using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IChatSessionPluginRepository
{
    Task<List<ChatSessionPlugin>> GetActivatedPluginsAsync(Guid chatSessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatSessionPlugin>> GetByChatSessionIdAsync(Guid chatSessionId,
        CancellationToken cancellationToken);
    
    Task<ChatSessionPlugin> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(ChatSessionPlugin chatSessionPlugin, CancellationToken cancellationToken);
    Task DeleteAsync(Guid chatPluginId, CancellationToken cancellationToken);
}