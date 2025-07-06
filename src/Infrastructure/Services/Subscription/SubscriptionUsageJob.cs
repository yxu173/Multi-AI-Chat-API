using Application.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Subscription;

public class SubscriptionUsageJob : ISubscriptionUsageJob
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<SubscriptionUsageJob> _logger;

    public SubscriptionUsageJob(
        ISubscriptionService subscriptionService,
        ILogger<SubscriptionUsageJob> logger)
    {
        _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task IncrementUsageAsync(Guid userId, double requestCost)
    {
        try
        {
            _logger.LogInformation("Background job: Incrementing usage for user {UserId} with cost {RequestCost}", userId, requestCost);
            await _subscriptionService.IncrementUserUsageAsync(userId, requestCost, CancellationToken.None);
            _logger.LogInformation("Background job: Successfully incremented usage for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background job: Failed to increment usage for user {UserId}", userId);
            throw; // Re-throw to let Hangfire handle retry logic
        }
    }
} 