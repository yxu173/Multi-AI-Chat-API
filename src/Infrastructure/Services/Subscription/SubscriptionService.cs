using Domain.Repositories;
using Domain.Aggregates.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Subscription;

public class SubscriptionService : ISubscriptionService
{
    private readonly IUserSubscriptionRepository _userSubscriptionRepository;
    private readonly ISubscriptionPlanRepository _subscriptionPlanRepository;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public SubscriptionService(
        IUserSubscriptionRepository userSubscriptionRepository,
        ISubscriptionPlanRepository subscriptionPlanRepository,
        ILogger<SubscriptionService> logger,
        IServiceProvider serviceProvider)
    {
        _userSubscriptionRepository = userSubscriptionRepository ?? throw new ArgumentNullException(nameof(userSubscriptionRepository));
        _subscriptionPlanRepository = subscriptionPlanRepository ?? throw new ArgumentNullException(nameof(subscriptionPlanRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task<(bool HasQuota, string? ErrorMessage)> CheckUserQuotaAsync(
        Guid userId, 
        double requestCost, 
        int requiredTokens = 0,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking quota for user {UserId} with request cost {RequestCost}", userId, requestCost);
        var subscription = await _userSubscriptionRepository.GetActiveSubscriptionAsync(userId, cancellationToken);
        
        if (subscription == null)
        {
            var freePlan = await _subscriptionPlanRepository.GetByNameAsync("Free Tier", cancellationToken) ?? SubscriptionPlan.CreateFreeTier();
            if (freePlan.Id == Guid.Empty)
            {
                await _subscriptionPlanRepository.AddAsync(freePlan, cancellationToken);
            }
            var userSubscription = UserSubscription.Create(userId, freePlan.Id, DateTime.UtcNow, DateTime.UtcNow.AddYears(1));
            await _userSubscriptionRepository.AddAsync(userSubscription, cancellationToken);
            subscription = userSubscription;
        }

        if (!subscription.IsActive || subscription.IsExpired())
        {
            return (false, "Your subscription has expired or is inactive. Please renew your subscription.");
        }

        var plan = await _subscriptionPlanRepository.GetByIdAsync(subscription.SubscriptionPlanId, cancellationToken);
        if (plan == null)
        {
            _logger.LogError("Subscription plan {PlanId} not found for user {UserId}", 
                subscription.SubscriptionPlanId, userId);
            return (false, "Invalid subscription plan. Please contact support.");
        }

        if (requiredTokens > 0 && requiredTokens > plan.MaxTokensPerRequest)
        {
            return (false, $"Request exceeds the token limit ({plan.MaxTokensPerRequest}) for your plan. Please reduce prompt size or upgrade your subscription.");
        }

        if (!subscription.HasAvailableQuota(plan.MaxRequestsPerMonth))
        {
            return (false, $"You've reached your monthly limit of {plan.MaxRequestsPerMonth} requests. Your quota will reset at the beginning of the next month.");
        }

        var subscriptionIdCapture = subscription.Id;
        var userIdCapture = userId;
        var requestCostCapture = requestCost; 
        
        _ = Task.Run(async () => 
        {
            using var scope = _serviceProvider.CreateScope();
            var subscriptionRepo = scope.ServiceProvider.GetRequiredService<IUserSubscriptionRepository>();
            
            try
            {
                var freshSubscription = await subscriptionRepo.GetByIdAsync(subscriptionIdCapture, CancellationToken.None);
                if (freshSubscription != null)
                {
                    freshSubscription.IncrementUsage(requestCostCapture); 
                    await subscriptionRepo.UpdateAsync(freshSubscription, CancellationToken.None);
                    _logger.LogInformation("Incremented usage for user {UserId}, subscription {SubscriptionId}, new usage: {Usage}, cost: {RequestCost}", userIdCapture, subscriptionIdCapture, freshSubscription.CurrentMonthUsage, requestCostCapture);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to increment usage for user {UserId}", userIdCapture);
            }
        });

        return (true, null);
    }

    public async Task ResetMonthlyUsageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _userSubscriptionRepository.ResetAllMonthlyUsageAsync(cancellationToken);
            _logger.LogInformation("Successfully reset monthly usage for all user subscriptions");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset monthly usage for user subscriptions");
            throw;
        }
    }
}
