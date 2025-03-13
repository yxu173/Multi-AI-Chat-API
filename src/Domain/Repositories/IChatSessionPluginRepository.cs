using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IChatSessionPluginRepository
{
    Task<List<ChatSessionPlugin>> GetActivatedPluginsAsync(Guid chatSessionId);
    Task AddAsync(ChatSessionPlugin chatSessionPlugin);

}