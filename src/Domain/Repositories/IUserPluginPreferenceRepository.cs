 using Domain.Aggregates.Users;

namespace Domain.Repositories;

public interface IUserPluginPreferenceRepository
{
    Task<List<UserPluginPreference>> GetAllByUserIdAsync(Guid userId);
    Task<UserPluginPreference> GetByUserIdAndPluginIdAsync(Guid userId, string pluginId);
    Task<UserPluginPreference> AddAsync(UserPluginPreference preference);
    Task<UserPluginPreference> UpdateAsync(UserPluginPreference preference);
}