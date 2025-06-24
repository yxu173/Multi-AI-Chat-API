using Domain.Aggregates.Chats;
using Domain.Aggregates.Llms;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class PluginRepository : IPluginRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PluginRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<Plugin>> GetAllAsync()
    {
        return await _dbContext.Plugins
            .ToListAsync();
    }

    public async Task<Plugin> GetByIdAsync(Guid id)
    {
        return await _dbContext.Plugins
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<IEnumerable<Plugin>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        return await _dbContext.Plugins
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();
    }

    public async Task AddAsync(Plugin plugin)
    {
        await _dbContext.Plugins.AddAsync(plugin);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateAsync(Plugin plugin)
    {
        _dbContext.Plugins.Update(plugin);
        await _dbContext.SaveChangesAsync();
    }
}