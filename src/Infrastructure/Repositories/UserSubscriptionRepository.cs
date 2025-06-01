using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Domain.Aggregates.Admin;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class UserSubscriptionRepository : IUserSubscriptionRepository
{
    private readonly ApplicationDbContext _dbContext;

    public UserSubscriptionRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<UserSubscription?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserSubscriptions
            .AsNoTracking()
            .Include(s => s.SubscriptionPlan)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<UserSubscription?> GetActiveSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        
        return await _dbContext.UserSubscriptions
            .AsNoTracking()
            .Include(s => s.SubscriptionPlan)
            .Where(s => s.UserId == userId && s.IsActive && s.StartDate <= now && s.ExpiryDate > now)
            .OrderByDescending(s => s.ExpiryDate) // Get the one that expires last if there are multiple
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserSubscriptions
            .AsNoTracking()
            .Include(s => s.SubscriptionPlan)
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserSubscription>> GetByPlanIdAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserSubscriptions
            .AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.SubscriptionPlanId == planId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserSubscription>> GetExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        
        return await _dbContext.UserSubscriptions
            .AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.SubscriptionPlan)
            .Where(s => s.IsActive && s.ExpiryDate <= now)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserSubscription> AddAsync(UserSubscription userSubscription, CancellationToken cancellationToken = default)
    {
        await _dbContext.UserSubscriptions.AddAsync(userSubscription, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return userSubscription;
    }

    public async Task<UserSubscription> UpdateAsync(UserSubscription userSubscription, CancellationToken cancellationToken = default)
    {
        _dbContext.Entry(userSubscription).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return userSubscription;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.UserSubscriptions.FindAsync(new object[] { id }, cancellationToken);
        if (subscription != null)
        {
            _dbContext.UserSubscriptions.Remove(subscription);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public Task ResetAllMonthlyUsageAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.UserSubscriptions
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.CurrentMonthUsage, 0)
                .SetProperty(u => u.LastUsageReset, DateTime.UtcNow.Date),
                cancellationToken);
    }
}
