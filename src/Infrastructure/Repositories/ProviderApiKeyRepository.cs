using Domain.Aggregates.Admin;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ProviderApiKeyRepository : IProviderApiKeyRepository
{
    private readonly ApplicationDbContext _dbContext;

    public ProviderApiKeyRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<ProviderApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProviderApiKeys
            .AsNoTracking()
            .Include(k => k.AiProvider)
            .FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderApiKey>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProviderApiKeys
            .AsNoTracking()
            .Include(k => k.AiProvider)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderApiKey>> GetActiveByProviderIdAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProviderApiKeys
            .AsNoTracking()
            .Include(k => k.AiProvider)
            .Where(k => k.AiProviderId == providerId && k.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProviderApiKey?> GetNextAvailableKeyAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var availableKey = await _dbContext.ProviderApiKeys
            .Where(k => k.AiProviderId == providerId && k.IsActive &&
                        (!k.IsRateLimited || k.RateLimitedUntil <= now) &&
                        k.UsageCountToday < k.MaxRequestsPerDay)
            .OrderBy(k => k.LastUsedTimestamp) 
            .FirstOrDefaultAsync(cancellationToken);

        if (availableKey != null)
        {
            availableKey.GetType().GetProperty("LastUsedTimestamp")!.SetValue(availableKey, now);
             _dbContext.Entry(availableKey).State = EntityState.Modified;
            await _dbContext.SaveChangesAsync(cancellationToken); 
        }
        
        return availableKey;
    }

    public async Task<ProviderApiKey> AddAsync(ProviderApiKey providerApiKey, CancellationToken cancellationToken = default)
    {
        await _dbContext.ProviderApiKeys.AddAsync(providerApiKey, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return providerApiKey;
    }

    public async Task<ProviderApiKey> UpdateAsync(ProviderApiKey providerApiKey, CancellationToken cancellationToken = default)
    {
        _dbContext.Entry(providerApiKey).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return providerApiKey;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var apiKey = await _dbContext.ProviderApiKeys.FindAsync(new object[] { id }, cancellationToken);
        if (apiKey != null)
        {
            _dbContext.ProviderApiKeys.Remove(apiKey);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ResetAllDailyUsageAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.ProviderApiKeys
            .ExecuteUpdateAsync(s => s
                .SetProperty(k => k.UsageCountToday, 0) 
                .SetProperty(k => k.IsRateLimited, false)
                .SetProperty(k => k.RateLimitedUntil, (DateTime?)null),
                cancellationToken);
    }

    
    public async Task MarkAsRateLimitedAsync(Guid apiKeyId, DateTime rateLimitedUntil, CancellationToken cancellationToken = default)
    {
        var apiKey = await _dbContext.ProviderApiKeys.FindAsync(new object[] { apiKeyId }, cancellationToken);
        if (apiKey != null)
        {
            apiKey.MarkAsRateLimited(rateLimitedUntil);
            _dbContext.Entry(apiKey).State = EntityState.Modified;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ClearRateLimitAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        var apiKey = await _dbContext.ProviderApiKeys.FindAsync(new object[] { apiKeyId }, cancellationToken);
        if (apiKey != null)
        {
            apiKey.ClearRateLimit();
            _dbContext.Entry(apiKey).State = EntityState.Modified;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task IncrementUsageAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        var apiKey = await _dbContext.ProviderApiKeys.FindAsync(new object[] { apiKeyId }, cancellationToken);
        if (apiKey != null)
        {
            apiKey.UpdateUsage();
            _dbContext.Entry(apiKey).State = EntityState.Modified;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ClearExpiredRateLimitsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expiredKeys = await _dbContext.ProviderApiKeys
            .Where(k => k.IsRateLimited && k.RateLimitedUntil <= now)
            .ToListAsync(cancellationToken);

        if (expiredKeys.Any())
        {
            foreach (var key in expiredKeys)
            {
                key.ClearRateLimit();
                _dbContext.Entry(key).State = EntityState.Modified;
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
