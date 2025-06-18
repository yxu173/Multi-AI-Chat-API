namespace Application.Abstractions.Interfaces;

public interface ISubscriptionUsageManager
{
    Task IncrementUsageAsync(Guid subscriptionId, double cost);
} 