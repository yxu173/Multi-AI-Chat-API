using Domain.Aggregates.Users;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class UserPluginPreferenceRepository : IUserPluginPreferenceRepository
{
    private readonly ApplicationDbContext _dbContext;

    public UserPluginPreferenceRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<UserPluginPreference>> GetAllByUserIdAsync(Guid userId)
    {
        return await _dbContext.UserPluginPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync();
    }

    public async Task<UserPluginPreference> GetByUserIdAndPluginIdAsync(Guid userId, string pluginId)
    {
        return await _dbContext.UserPluginPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.PluginId == pluginId);
    }

    public async Task<UserPluginPreference> AddAsync(UserPluginPreference preference)
    {
        _dbContext.UserPluginPreferences.Add(preference);
        await _dbContext.SaveChangesAsync();
        return preference;
    }

    public async Task<UserPluginPreference> UpdateAsync(UserPluginPreference preference)
    {
        _dbContext.UserPluginPreferences.Update(preference);
        await _dbContext.SaveChangesAsync();
        return preference;
    }
}