using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Domain.Aggregates.Admin;

namespace Domain.Repositories;

public interface ISubscriptionPlanRepository
{
    Task<SubscriptionPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionPlan>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionPlan>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionPlan> AddAsync(SubscriptionPlan subscriptionPlan, CancellationToken cancellationToken = default);
    Task<SubscriptionPlan> UpdateAsync(SubscriptionPlan subscriptionPlan, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SubscriptionPlan?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
}
