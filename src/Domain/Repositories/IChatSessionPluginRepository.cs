using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IChatSessionPluginRepository
{
    Task<List<ChatSessionPlugin>> GetActivatedPluginsAsync(Guid chatSessionId, CancellationToken cancellationToken);
    Task AddAsync(ChatSessionPlugin chatSessionPlugin, CancellationToken cancellationToken);

}