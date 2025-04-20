using Domain.Aggregates.Chats;

namespace Domain.Repositories;

public interface IPluginRepository
{
    Task<IEnumerable<Plugin>> GetAllAsync();
    Task<Plugin> GetByIdAsync(Guid id);
    Task<IEnumerable<Plugin>> GetByIdsAsync(IEnumerable<Guid> ids);
    Task AddAsync(Plugin plugin);
    Task UpdateAsync(Plugin plugin);
}