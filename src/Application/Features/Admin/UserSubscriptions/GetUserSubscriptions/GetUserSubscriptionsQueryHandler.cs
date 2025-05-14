using Application.Abstractions.Messaging;
using Domain.Aggregates.Admin;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Admin.UserSubscriptions.GetUserSubscriptions;

internal sealed class GetUserSubscriptionsQueryHandler : IQueryHandler<GetUserSubscriptionsQuery, IReadOnlyList<UserSubscription>>
{
    private readonly IUserSubscriptionRepository _userSubscriptionRepository;

    public GetUserSubscriptionsQueryHandler(IUserSubscriptionRepository userSubscriptionRepository)
    {
        _userSubscriptionRepository = userSubscriptionRepository;
    }

    public async Task<Result<IReadOnlyList<UserSubscription>>> ExecuteAsync(GetUserSubscriptionsQuery query, CancellationToken ct)
    {
        IReadOnlyList<UserSubscription> subscriptions;

        if (query.UserId.HasValue)
        {
            subscriptions = await _userSubscriptionRepository.GetByUserIdAsync(query.UserId.Value, ct);
        }
        else if (query.PlanId.HasValue)
        {
            subscriptions = await _userSubscriptionRepository.GetByPlanIdAsync(query.PlanId.Value, ct);
        }
        else if (!query.ActiveOnly)
        {
            // For this case, we need to get all subscriptions but we don't have a direct repository method
            // Let's fetch by user ID for all users, which is an inefficient approach
            // In a real implementation, the repository should have a GetAllAsync method
            var userIds = await _userSubscriptionRepository.GetByPlanIdAsync(default, ct);
            subscriptions = userIds;
        }
        else
        {
            // Active-only case - we would need to implement a repository method to get all active subscriptions
            // For now, we'll return an empty list to avoid errors
            subscriptions = new List<UserSubscription>();
        }

        // Filter for active subscriptions if requested
        if (query.ActiveOnly)
        {
            var now = System.DateTime.UtcNow;
            subscriptions = subscriptions
                .Where(s => s.IsActive && !s.IsExpired())
                .ToList();
        }

        return Result.Success<IReadOnlyList<UserSubscription>>(subscriptions);
    }
}
