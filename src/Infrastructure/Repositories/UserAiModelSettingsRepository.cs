using Domain.Aggregates.Users;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Infrastructure.Services.Caching;
using Application.Abstractions.Interfaces;

namespace Infrastructure.Repositories;

public class UserAiModelSettingsRepository : IUserAiModelSettingsRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ICacheService _cacheService;
    private const string CacheKeyPrefix = "user:settings";
    private readonly TimeSpan CacheExpiry = TimeSpan.FromHours(1);

    public UserAiModelSettingsRepository(ApplicationDbContext context, ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    public async Task<UserAiModelSettings?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"{CacheKeyPrefix}:id:{id}";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _context.UserAiModelSettings.FirstOrDefaultAsync(s => s.Id == id, cancellationToken),
            CacheExpiry,
            cancellationToken);
    }

    public async Task<UserAiModelSettings?> GetByUserAndModelIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"{CacheKeyPrefix}:user:{userId}";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _context.UserAiModelSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken),
            CacheExpiry,
            cancellationToken);
    }

    public async Task<IEnumerable<UserAiModelSettings>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"{CacheKeyPrefix}:user:all:{userId}";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _context.UserAiModelSettings.Where(s => s.UserId == userId).ToListAsync(cancellationToken),
            CacheExpiry,
            cancellationToken);
    }

    public async Task<UserAiModelSettings?> GetDefaultByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"{CacheKeyPrefix}:user:default:{userId}";
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async () => await _context.UserAiModelSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken),
            CacheExpiry,
            cancellationToken);
    }

    public async Task<UserAiModelSettings> AddAsync(UserAiModelSettings settings, CancellationToken cancellationToken = default)
    {
        _context.UserAiModelSettings.Add(settings);
        await _context.SaveChangesAsync(cancellationToken);
        // Invalidate cache for this user
        await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:user:*:{settings.UserId}", cancellationToken);
        return settings;
    }

    public async Task<UserAiModelSettings> UpdateAsync(UserAiModelSettings settings, CancellationToken cancellationToken = default)
    {
        _context.Entry(settings).State = EntityState.Modified;
        await _context.SaveChangesAsync(cancellationToken);
        // Invalidate cache for this user
        await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:user:*:{settings.UserId}", cancellationToken);
        await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:id:{settings.Id}", cancellationToken);
        return settings;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var settings = await GetByIdAsync(id, cancellationToken);
        if (settings != null)
        {
            _context.UserAiModelSettings.Remove(settings);
            await _context.SaveChangesAsync(cancellationToken);
            // Invalidate cache for this user
            await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:user:*:{settings.UserId}", cancellationToken);
            await _cacheService.EvictByPatternAsync($"{CacheKeyPrefix}:id:{id}", cancellationToken);
        }
    }
}
