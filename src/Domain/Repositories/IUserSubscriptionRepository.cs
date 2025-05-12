using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Domain.Aggregates.Admin;

namespace Domain.Repositories;

public interface IUserSubscriptionRepository
{
    Task<UserSubscription?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserSubscription?> GetActiveSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSubscription>> GetByPlanIdAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSubscription>> GetExpiredAsync(CancellationToken cancellationToken = default);
    Task<UserSubscription> AddAsync(UserSubscription userSubscription, CancellationToken cancellationToken = default);
    Task<UserSubscription> UpdateAsync(UserSubscription userSubscription, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task ResetAllDailyUsageAsync(CancellationToken cancellationToken = default);
}
