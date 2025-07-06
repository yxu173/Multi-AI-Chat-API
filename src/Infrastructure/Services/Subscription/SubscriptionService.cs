using Application.Abstractions.Interfaces;
using Domain.Repositories;
using Domain.Aggregates.Admin;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Subscription;

public class SubscriptionService : ISubscriptionService
{
    private readonly IUserSubscriptionRepository _userSubscriptionRepository;
    private readonly ISubscriptionPlanRepository _subscriptionPlanRepository;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IUserSubscriptionRepository userSubscriptionRepository,
        ISubscriptionPlanRepository subscriptionPlanRepository,
        ILogger<SubscriptionService> logger)
    {
        _userSubscriptionRepository = userSubscriptionRepository ?? throw new ArgumentNullException(nameof(userSubscriptionRepository));
        _subscriptionPlanRepository = subscriptionPlanRepository ?? throw new ArgumentNullException(nameof(subscriptionPlanRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        return (true, null);
    }

    public async Task IncrementUserUsageAsync(Guid userId, double requestCost, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscription = await _userSubscriptionRepository.GetActiveSubscriptionAsync(userId, cancellationToken);
            if (subscription == null)
            {
                _logger.LogWarning("No active subscription found for user {UserId} when trying to increment usage", userId);
                return;
            }

            subscription.IncrementUsage(requestCost);
            await _userSubscriptionRepository.UpdateAsync(subscription, cancellationToken);
            
            _logger.LogInformation("Incremented usage for user {UserId}, subscription {SubscriptionId}, new usage: {Usage}, cost: {RequestCost}", 
                userId, subscription.Id, subscription.CurrentMonthUsage, requestCost);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment usage for user {UserId}", userId);
            throw;
        }
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