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
        // Strategy: Get a key with available quota, prioritizing those with less usage
        return await _dbContext.ProviderApiKeys
            .Where(k => k.AiProviderId == providerId && k.IsActive && k.DailyUsage < k.DailyQuota)
            .OrderBy(k => (double)k.DailyUsage / k.DailyQuota) // Use the key with the lowest usage percentage
            .FirstOrDefaultAsync(cancellationToken);
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
                .SetProperty(k => k.DailyUsage, 0),
                cancellationToken);
    }
}
