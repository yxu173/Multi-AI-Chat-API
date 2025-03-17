using Domain.Aggregates.Users;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class UserPluginRepository : IUserPluginRepository
{
    private readonly ApplicationDbContext _dbContext;

    public UserPluginRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<UserPlugin>> GetAllByUserIdAsync(Guid userId)
    {
        return await _dbContext.UserPlugins
            .Where(p => p.UserId == userId)
            .ToListAsync();
    }

    public async Task<UserPlugin> GetByUserIdAndPluginIdAsync(Guid userId, Guid pluginId)
    {
        return await _dbContext.UserPlugins
            .FirstOrDefaultAsync(p => p.UserId == userId && p.PluginId == pluginId);
    }

    public async Task<UserPlugin> AddAsync(UserPlugin preference)
    {
        _dbContext.UserPlugins.Add(preference);
        await _dbContext.SaveChangesAsync();
        return preference;
    }

    public async Task<UserPlugin> UpdateAsync(UserPlugin preference)
    {
        _dbContext.UserPlugins.Update(preference);
        await _dbContext.SaveChangesAsync();
        return preference;
    }
}