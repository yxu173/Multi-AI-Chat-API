using Domain.Aggregates.Chats;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class AiProviderRepository : IAiProviderRepository
{
    private readonly ApplicationDbContext _dbContext;

    public AiProviderRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AiProvider?> GetByIdAsync(Guid id)
    {
        return await _dbContext.AiProviders
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<IReadOnlyList<AiProvider>> GetAllAsync()
    {
        return await _dbContext.AiProviders
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AiProvider>> GetEnabledAsync()
    {
        return await _dbContext.AiProviders
            .Where(p => p.IsEnabled)
            .ToListAsync();
    }

    public async Task AddAsync(AiProvider aiProvider)
    {
        await _dbContext.AiProviders.AddAsync(aiProvider);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateAsync(AiProvider aiProvider)
    {
        _dbContext.AiProviders.Update(aiProvider);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var provider = await _dbContext.AiProviders.FindAsync(id);
        if (provider == null)
        {
            return false;
        }

        _dbContext.AiProviders.Remove(provider);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _dbContext.AiProviders.AnyAsync(p => p.Id == id);
    }

    public async Task<bool> ExistsByNameAsync(string name)
    {
        return await _dbContext.AiProviders.AnyAsync(p => p.Name == name);
    }
}