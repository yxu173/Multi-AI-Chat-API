using Domain.Repositories;
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
        int requiredTokens = 0,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _userSubscriptionRepository.GetActiveSubscriptionAsync(userId, cancellationToken);
        
        //TODO : Need To Handle Well
        // If no subscription is found, use default free tier limits
        if (subscription == null)
        {
            _logger.LogInformation("No active subscription found for user {UserId}. Using free tier limits.", userId);
            // Default free tier: 10 requests per day, max 2000 tokens per request
            return requiredTokens <= 2000 ? 
                (true, null) : 
                (false, "Exceeded token limit for free tier. Please upgrade your subscription.");
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

        if (!subscription.HasAvailableQuota(plan.MaxRequestsPerDay))
        {
            return (false, $"You've reached your daily limit of {plan.MaxRequestsPerDay} requests. Your quota will reset tomorrow.");
        }

     
        var subscriptionId = subscription.Id;
        var userIdCapture = userId;
        
        _ = Task.Run(async () => 
        {
            using var scope = _serviceProvider.CreateScope();
            var subscriptionRepo = scope.ServiceProvider.GetRequiredService<IUserSubscriptionRepository>();
            
            try
            {
                var freshSubscription = await subscriptionRepo.GetByIdAsync(subscriptionId, CancellationToken.None);
                if (freshSubscription != null)
                {
                    freshSubscription.IncrementUsage();
                    await subscriptionRepo.UpdateAsync(freshSubscription, CancellationToken.None);
                    _logger.LogDebug("Updated usage for user {UserId}, subscription {SubscriptionId}", userIdCapture, subscriptionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to increment usage for user {UserId}", userIdCapture);
            }
        });

        return (true, null);
    }

    public async Task ResetDailyUsageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _userSubscriptionRepository.ResetAllDailyUsageAsync(cancellationToken);
            _logger.LogInformation("Successfully reset daily usage for all user subscriptions");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset daily usage for user subscriptions");
            throw;
        }
    }
}
