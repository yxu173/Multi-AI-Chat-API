using Application.Abstractions.Interfaces;
using Domain.Repositories;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;

namespace Infrastructure.Services.Subscription
{
    public class SubscriptionUsageManager : ISubscriptionUsageManager
    {
        private readonly IUserSubscriptionRepository _subscriptionRepository;
        private readonly ILogger<SubscriptionUsageManager> _logger;

        public SubscriptionUsageManager(IUserSubscriptionRepository subscriptionRepository, ILogger<SubscriptionUsageManager> logger)
        {
            _subscriptionRepository = subscriptionRepository;
            _logger = logger;
        }

        public async Task IncrementUsageAsync(Guid subscriptionId, double cost)
        {
            try
            {
                var subscription = await _subscriptionRepository.GetByIdAsync(subscriptionId, CancellationToken.None);
                if (subscription != null)
                {
                    subscription.IncrementUsage(cost);
                    await _subscriptionRepository.UpdateAsync(subscription, CancellationToken.None);
                    _logger.LogInformation("Incremented usage for subscription {SubscriptionId}, new usage: {Usage}, cost: {RequestCost}", subscriptionId, subscription.CurrentMonthUsage, cost);
                }
                else
                {
                    _logger.LogWarning("Subscription with ID {SubscriptionId} not found for usage increment.", subscriptionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to increment usage for subscription {SubscriptionId}", subscriptionId);
            }
        }
    }
} 