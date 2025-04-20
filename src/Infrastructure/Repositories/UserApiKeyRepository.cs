using Domain.Aggregates.Users;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class UserApiKeyRepository : IUserApiKeyRepository
{
    private readonly ApplicationDbContext _dbContext;

    public UserApiKeyRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<UserApiKey?> GetByIdAsync(Guid id)
    {
        return await _dbContext.UserApiKeys
            .AsNoTracking()
            .Include(k => k.User)
            .FirstOrDefaultAsync(k => k.Id == id);
    }

    public Task<UserApiKey> GetByUserAndProviderAsync(Guid userId, Guid providerId)
    {
        return _dbContext.UserApiKeys
           .AsNoTracking()
           .Include(k => k.User)
           .FirstOrDefaultAsync(k => k.UserId == userId && k.AiProviderId == providerId);
    }

    public async Task<IEnumerable<UserApiKey>> GetByUserIdAsync(Guid userId)
    {
        return await _dbContext.UserApiKeys
            .AsNoTracking()
            .Where(k => k.UserId == userId)
            .ToListAsync();
    }
    

    public async Task<IReadOnlyList<UserApiKey>> GetByProviderIdAsync(Guid providerId)
    {
        return await _dbContext.UserApiKeys
            .AsNoTracking()
            .Where(k => k.AiProviderId == providerId)
            .ToListAsync();
    }

    public async Task AddAsync(UserApiKey userApiKey)
    {
        if (userApiKey == null)
        {
            throw new ArgumentNullException(nameof(userApiKey));
        }
        
        
     
        
        await _dbContext.UserApiKeys.AddAsync(userApiKey);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateAsync(UserApiKey userApiKey)
    {
        if (userApiKey == null)
        {
            throw new ArgumentNullException(nameof(userApiKey));
        }
        
     
        
        _dbContext.Entry(userApiKey).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var apiKey = await _dbContext.UserApiKeys.FindAsync(id);
        if (apiKey == null)
        {
            return false;
        }

        _dbContext.UserApiKeys.Remove(apiKey);
        await _dbContext.SaveChangesAsync();
        return true;
    }
  
}