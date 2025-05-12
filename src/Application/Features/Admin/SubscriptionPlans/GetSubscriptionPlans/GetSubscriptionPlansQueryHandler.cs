using Application.Abstractions.Messaging;
using Domain.Aggregates.Admin;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Admin.SubscriptionPlans.GetSubscriptionPlans;

internal sealed class GetSubscriptionPlansQueryHandler : IQueryHandler<GetSubscriptionPlansQuery, IReadOnlyList<SubscriptionPlan>>
{
    private readonly ISubscriptionPlanRepository _subscriptionPlanRepository;

    public GetSubscriptionPlansQueryHandler(ISubscriptionPlanRepository subscriptionPlanRepository)
    {
        _subscriptionPlanRepository = subscriptionPlanRepository;
    }

    public async Task<Result<IReadOnlyList<SubscriptionPlan>>> ExecuteAsync(GetSubscriptionPlansQuery query, CancellationToken ct)
    {
        if (query.ActiveOnly)
        {
            var activePlans = await _subscriptionPlanRepository.GetActiveAsync(ct);
            return Result.Success<IReadOnlyList<SubscriptionPlan>>(activePlans);
        }
        else
        {
            var allPlans = await _subscriptionPlanRepository.GetAllAsync(ct);
            return Result.Success<IReadOnlyList<SubscriptionPlan>>(allPlans);
        }
    }
}
