 using Domain.Aggregates.Users;

namespace Domain.Repositories;

public interface IUserPluginRepository
{
    Task<IEnumerable<UserPlugin>> GetAllByUserIdAsync(Guid userId);
    Task<UserPlugin> GetByUserIdAndPluginIdAsync(Guid userId, Guid pluginId);
    Task<UserPlugin> AddAsync(UserPlugin preference);
    Task<UserPlugin> UpdateAsync(UserPlugin preference);
}