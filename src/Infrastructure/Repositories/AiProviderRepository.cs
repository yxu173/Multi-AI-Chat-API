using Domain.Aggregates.Chats;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions.Interfaces;

namespace Infrastructure.Repositories;

public class AiProviderRepository : IAiProviderRepository
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICacheService _cacheService;
    private const string CacheKeyPrefix = "aiProviders";
    private readonly TimeSpan CacheExpiry = TimeSpan.FromDays(30);

    public AiProviderRepository(ApplicationDbContext dbContext, ICacheService cacheService)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
    }

    public async Task<AiProvider?> GetByIdAsync(Guid id)
    {
        string cacheKey = $"{CacheKeyPrefix}:{id}";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _dbContext.AiProviders.FirstOrDefaultAsync(p => p.Id == id),
            CacheExpiry);
    }

    public async Task<IReadOnlyList<AiProvider>> GetAllAsync()
    {
        string cacheKey = $"{CacheKeyPrefix}:all";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _dbContext.AiProviders.ToListAsync(),
            CacheExpiry);
    }

    public async Task<IReadOnlyList<AiProvider>> GetEnabledAsync()
    {
        string cacheKey = $"{CacheKeyPrefix}:enabled";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _dbContext.AiProviders.Where(p => p.IsEnabled).ToListAsync(),
            CacheExpiry);
    }

    public async Task AddAsync(AiProvider aiProvider)
    {
        await _dbContext.AiProviders.AddAsync(aiProvider);
        await _dbContext.SaveChangesAsync();
        await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:*");
    }

    public async Task UpdateAsync(AiProvider aiProvider)
    {
        _dbContext.AiProviders.Update(aiProvider);
        await _dbContext.SaveChangesAsync();
        await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:*");
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
        await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:*");
        return true;
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        string cacheKey = $"{CacheKeyPrefix}:exists:{id}";
        return await _dbContext.AiProviders.AnyAsync(p => p.Id == id);
    }

    public async Task<bool> ExistsByNameAsync(string name)
    {
        string cacheKey = $"{CacheKeyPrefix}:existsByName:{name}";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _dbContext.AiProviders.AnyAsync(p => p.Name == name),
            CacheExpiry);
    }
}