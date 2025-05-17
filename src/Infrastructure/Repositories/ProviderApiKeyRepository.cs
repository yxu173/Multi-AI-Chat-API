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
        // Strategy: Get an active key for the provider that:
        // 1. Is not currently rate-limited OR its rate limit has expired.
        // 2. Has available daily quota.
        // Prioritize the least recently used key to distribute load.
        // Atomically update LastUsedTimestamp upon selection to minimize race conditions.

        var now = DateTime.UtcNow;
        var availableKey = await _dbContext.ProviderApiKeys
            .Where(k => k.AiProviderId == providerId && k.IsActive &&
                        (!k.IsRateLimited || k.RateLimitedUntil <= now) &&
                        k.UsageCountToday < k.MaxRequestsPerDay)
            .OrderBy(k => k.LastUsedTimestamp) // Oldest used first
            .FirstOrDefaultAsync(cancellationToken);

        if (availableKey != null)
        {
            // Immediately mark as used to reduce chance of other requests picking it
            // In a high concurrency scenario, a more robust distributed lock or optimistic concurrency
            // with retry on conflict would be needed for true atomicity.
            // This is a simpler approach for now.
            availableKey.GetType().GetProperty("LastUsedTimestamp")!.SetValue(availableKey, now);
            // We don't call UpdateUsage() here as that's the responsibility of ProviderKeyManagementService
            // after a successful API call. We just update the timestamp to affect subsequent GetNextAvailableKeyAsync calls.
             _dbContext.Entry(availableKey).State = EntityState.Modified;
            await _dbContext.SaveChangesAsync(cancellationToken); // Save the timestamp update
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
                .SetProperty(k => k.UsageCountToday, 0) // Changed from DailyUsage
                .SetProperty(k => k.IsRateLimited, false)
                .SetProperty(k => k.RateLimitedUntil, (DateTime?)null),
                cancellationToken);
    }

    // New method implementations
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
            apiKey.UpdateUsage(); // This method in the entity now updates UsageCountToday and LastUsedTimestamp
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
