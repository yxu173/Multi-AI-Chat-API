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

public class SubscriptionPlanRepository : ISubscriptionPlanRepository
{
    private readonly ApplicationDbContext _dbContext;

    public SubscriptionPlanRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<SubscriptionPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SubscriptionPlans
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<SubscriptionPlan> AddAsync(SubscriptionPlan subscriptionPlan, CancellationToken cancellationToken = default)
    {
        await _dbContext.SubscriptionPlans.AddAsync(subscriptionPlan, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return subscriptionPlan;
    }

    public async Task<SubscriptionPlan> UpdateAsync(SubscriptionPlan subscriptionPlan, CancellationToken cancellationToken = default)
    {
        _dbContext.Entry(subscriptionPlan).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return subscriptionPlan;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.SubscriptionPlans.FindAsync(new object[] { id }, cancellationToken);
        if (plan != null)
        {
            _dbContext.SubscriptionPlans.Remove(plan);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<SubscriptionPlan?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _dbContext.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == name, cancellationToken);
    }
}
